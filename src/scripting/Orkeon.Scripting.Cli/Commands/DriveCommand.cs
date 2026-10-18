using System.Globalization;
using System.Text;
using System.Text.Json;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Orkeon.Hosting;
using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Drive;
using Orkeon.Tools.Drive.Administration;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Options shared by the <c>orkeon drive</c> verbs.</summary>
internal abstract class DriveCommandOptionsBase : CliOptions
{
    /// <summary>Path to appsettings.json holding <c>Orkeon:Tools:Drive</c>.</summary>
    [Option('s', "settings", Required = false,
        HelpText = "Path to appsettings.json (defaults to the current directory's resolution chain, like `orkeon run`).")]
    public string? SettingsPath { get; set; }

    /// <summary>Test seam: overrides the current directory the settings chain is anchored at.</summary>
    internal string? WorkingDirectoryOverride { get; set; }

    /// <summary>Test seam: services registered after the runner host's, so doubles replace the real ones.</summary>
    internal Action<HostBuilderContext, IServiceCollection>? ConfigureTestServices { get; set; }
}

/// <summary>Parsed options of <c>orkeon drive accounts</c>.</summary>
[Verb("accounts", HelpText = "List the configured drive accounts (Google Drive, OneDrive), their rights, root folder and whether each is ready (no network).")]
internal sealed class DriveAccountsCommandOptions : DriveCommandOptionsBase
{
    /// <summary>Writes the list as JSON.</summary>
    [Option("json", Required = false, Default = false, HelpText = "Write the list as JSON.")]
    public bool Json { get; set; }
}

/// <summary>Parsed options of <c>orkeon drive login</c>.</summary>
[Verb("login", HelpText = "Sign a drive account in (OneDrive with a device code, Google Drive in the browser) and store its tokens.")]
internal sealed class DriveLoginCommandOptions : DriveCommandOptionsBase
{
    /// <summary>The account to sign in.</summary>
    [Value(0, Required = true, MetaName = "account", HelpText = "The account name, as declared under Orkeon:Tools:Drive:Accounts.")]
    public string Account { get; set; } = string.Empty;

    /// <summary><c>--events jsonl</c>: the sign-in as protocol lines on stdout, for a program that drives it.</summary>
    [Option("events", Required = false,
        HelpText = "jsonl: write each step of the sign-in as one event line on stdout, read the pasted redirect address on stdin, and abort when stdin closes (the way Orkeon Studio drives it).")]
    public string? Events { get; set; }

    /// <summary>Test seam: the terminal interaction.</summary>
    internal IOAuthLoginInteraction? Interaction { get; set; }
}

/// <summary>Parsed options of <c>orkeon drive logout</c>.</summary>
[Verb("logout", HelpText = "Forget the stored OAuth tokens of a drive account.")]
internal sealed class DriveLogoutCommandOptions : DriveCommandOptionsBase
{
    /// <summary>The account to sign out.</summary>
    [Value(0, Required = true, MetaName = "account", HelpText = "The account name.")]
    public string Account { get; set; } = string.Empty;
}

/// <summary>Parsed options of <c>orkeon drive check</c>.</summary>
[Verb("check", HelpText = "Connect to a drive account, find its root folder and list its first items.")]
internal sealed class DriveCheckCommandOptions : DriveCommandOptionsBase
{
    /// <summary>The account to check.</summary>
    [Value(0, Required = true, MetaName = "account", HelpText = "The account name.")]
    public string Account { get; set; } = string.Empty;

    /// <summary><c>--events jsonl</c>: the verdict as one event line on stdout, for a program that drives the check.</summary>
    [Option("events", Required = false,
        HelpText = "jsonl: write the verdict as one event line on stdout — what the root folder holds, or the error with its code (the way Orkeon Studio drives it).")]
    public string? Events { get; set; }
}

/// <summary>
/// <c>orkeon drive accounts | login | logout | check</c> — the operator's side of the drive tool
/// family (DRIVE-05): what is configured, the one interactive step every account needs, and a
/// connection check. Builds the shared runner host over the resolved settings file, so the OAuth
/// tokens land where every run will look for them.
/// </summary>
internal static class DriveCommand
{
    private const string EventsFormat = "jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Parses <paramref name="args"/> (already stripped of the leading <c>drive</c>) and dispatches to a verb.</summary>
    public static async Task<int> DispatchAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return await CliGrammar.DispatchAsync("drive", args, [typeof(DriveAccountsCommandOptions), typeof(DriveLoginCommandOptions), typeof(DriveLogoutCommandOptions), typeof(DriveCheckCommandOptions)], options => options switch
        {
            DriveAccountsCommandOptions o => ExecuteAccountsAsync(o),
            DriveLoginCommandOptions o => ExecuteLoginAsync(o),
            DriveLogoutCommandOptions o => ExecuteLogoutAsync(o),
            DriveCheckCommandOptions o => ExecuteCheckAsync(o),
            _ => Task.FromResult(Program.ExitScriptError),
        }).ConfigureAwait(false);
    }

    /// <summary>Lists the accounts.</summary>
    public static Task<int> ExecuteAccountsAsync(DriveAccountsCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GuardedAsync("accounts", options, async (administration, ct) =>
        {
            var accounts = await administration.ListAsync(ct).ConfigureAwait(false);
            if (options.Json)
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(accounts, JsonOptions)).ConfigureAwait(false);
                return Program.ExitOk;
            }

            if (accounts.Count == 0)
            {
                await Console.Out.WriteLineAsync(
                    "No drive account is configured. Declare one under Orkeon:Tools:Drive:Accounts in the settings file (see docs/guides/drive.md).")
                    .ConfigureAwait(false);
                return Program.ExitOk;
            }

            await Console.Out.WriteAsync(RenderAccounts(accounts)).ConfigureAwait(false);
            return Program.ExitOk;
        });
    }

    /// <summary>
    /// Signs an account in. With <c>--events jsonl</c> the steps are event lines on standard
    /// output, the pasted redirect address is read on standard input, and the end of that input
    /// aborts the sign-in — as <c>orkeon email login</c> does for Studio's Mails tab.
    /// </summary>
    public static async Task<int> ExecuteLoginAsync(DriveLoginCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (await RefuseFormatAsync("login", options.Events).ConfigureAwait(false))
            return Program.ExitScriptError;

        var events = options.Events is null ? null : new DriveEventWriter(Console.Out);
        return await GuardedAsync("login", options, async (administration, ct) =>
        {
            if (events is null)
            {
                var interaction = options.Interaction ?? new ConsoleOAuthLoginInteraction("drive");
                await administration.LoginAsync(options.Account, interaction, ct).ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"Signed in: the tokens of drive account '{options.Account}' are stored. Agents can use it now.")
                    .ConfigureAwait(false);
                return Program.ExitOk;
            }

            using var driver = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var dialogue = new EventOAuthLoginInteraction(events, Console.In, onInputClosed: () =>
            {
                try
                {
                    driver.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The sign-in ended before the driver closed the input; nothing left to abort.
                }
            });

            try
            {
                await administration.LoginAsync(options.Account, dialogue, driver.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (driver.IsCancellationRequested)
            {
                // The driver left, or Ctrl+C: the same end, and nothing to say to a reader that is gone.
                return Program.ExitCancelled;
            }

            events.LoginCompleted(options.Account);
            return Program.ExitOk;
        }, events).ConfigureAwait(false);
    }

    /// <summary>Signs an account out.</summary>
    public static Task<int> ExecuteLogoutAsync(DriveLogoutCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GuardedAsync("logout", options, async (administration, ct) =>
        {
            var removed = await administration.LogoutAsync(options.Account, ct).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(removed
                    ? $"Signed out: the tokens of drive account '{options.Account}' are deleted."
                    : $"Drive account '{options.Account}' had no stored tokens.")
                .ConfigureAwait(false);
            return Program.ExitOk;
        });
    }

    /// <summary>Checks that an account connects; with <c>--events jsonl</c> the verdict is one event line.</summary>
    public static async Task<int> ExecuteCheckAsync(DriveCheckCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (await RefuseFormatAsync("check", options.Events).ConfigureAwait(false))
            return Program.ExitScriptError;

        var events = options.Events is null ? null : new DriveEventWriter(Console.Out);
        return await GuardedAsync("check", options, async (administration, ct) =>
        {
            var result = await administration.CheckAsync(options.Account, ct).ConfigureAwait(false);
            var summary = RenderCheck(result);
            if (events is null)
                await Console.Out.WriteLineAsync(summary).ConfigureAwait(false);
            else
                events.CheckCompleted(result.Account, result.RootPath, result.RootName, result.Items, result.More, summary);
            return Program.ExitOk;
        }, events).ConfigureAwait(false);
    }

    /// <summary>Says, and answers true, when <paramref name="format"/> is an events format the verbs do not write.</summary>
    private static async Task<bool> RefuseFormatAsync(string verb, string? format)
    {
        if (format is null || string.Equals(format, EventsFormat, StringComparison.OrdinalIgnoreCase))
            return false;
        await Console.Error.WriteLineAsync($"orkeon drive {verb}: unsupported --events format '{format}' — the only one is {EventsFormat}.").ConfigureAwait(false);
        return true;
    }

    /// <summary>The sentence of a check that connected: the root folder and what it holds.</summary>
    internal static string RenderCheck(DriveCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var items = result.More
            ? string.Create(CultureInfo.InvariantCulture, $"more than {result.Items} item(s)")
            : string.Create(CultureInfo.InvariantCulture, $"{result.Items} item(s)");
        return $"Drive account '{result.Account}' is reachable: root folder {result.RootPath} ('{result.RootName}') holds {items}.";
    }

    /// <summary>The human listing of <paramref name="accounts"/>.</summary>
    internal static string RenderAccounts(IReadOnlyList<DriveAccountStatus> accounts)
    {
        var text = new StringBuilder();
        foreach (var account in accounts)
        {
            text.Append(account.Name);
            if (account.IsDefault)
                text.Append(" (default)");
            text.AppendLine();

            if (account.Rights is not null)
            {
                text.Append("  ").Append(account.Provider).Append(" · root ").AppendLine(account.Root);
                text.Append("  rights: ").AppendLine(account.Rights);
            }

            text.Append("  ").AppendLine(account.Ready ? "ready" : "NOT READY: " + account.Problem);
        }

        return text.ToString();
    }

    /// <summary>
    /// Top-level fault barrier: Ctrl+C → 130; what the operator fixes (configuration, unknown
    /// account, missing variable, sign-in needed) → 1; what the provider or the network did → 2.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Top-level CLI fault barrier: unexpected failures are converted to a runtime-error exit code so the tool reports cleanly instead of crashing with a stack trace.")]
    private static async Task<int> GuardedAsync(
        string verb,
        DriveCommandOptionsBase options,
        Func<DriveAccountAdministration, CancellationToken, Task<int>> body,
        DriveEventWriter? events = null)
    {
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            using var host = BuildHost(options);
            var administration = host.Services.GetRequiredService<DriveAccountAdministration>();
            return await body(administration, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Only the operator's Ctrl+C: a cancellation nobody asked for is a failure, below.
            return Program.ExitCancelled;
        }
        catch (DriveToolException ex)
        {
            await FailAsync(verb, events, ex.Code.ToString(), ex.Message).ConfigureAwait(false);
            return IsOperatorFixable(ex.Code) ? Program.ExitScriptError : Program.ExitRuntimeError;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FileNotFoundException or DirectoryNotFoundException)
        {
            await FailAsync(verb, events, nameof(DriveErrorCode.InvalidConfiguration), ex.Message).ConfigureAwait(false);
            return Program.ExitScriptError;
        }
        catch (Exception ex)
        {
            await FailAsync(verb, events, DriveEventWriter.UnexpectedCode, $"unexpected error [{ex.GetType().FullName}]: {ex.Message}").ConfigureAwait(false);
            if (RunnerEnvironment.DebugDiagnostics)
                await Console.Error.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            return Program.ExitRuntimeError;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>Says why the verb failed: an <c>error</c> event when a program drives it, one line on stderr otherwise.</summary>
    private static Task FailAsync(string verb, DriveEventWriter? events, string code, string message)
    {
        if (events is null)
            return Console.Error.WriteLineAsync($"orkeon drive {verb}: {message}");

        events.Error(code, message);
        return Task.CompletedTask;
    }

    private static bool IsOperatorFixable(DriveErrorCode code) => code is DriveErrorCode.NotConfigured
        or DriveErrorCode.UnknownAccount
        or DriveErrorCode.InvalidConfiguration
        or DriveErrorCode.CredentialMissing
        or DriveErrorCode.LoginRequired
        or DriveErrorCode.InvalidRequest
        or DriveErrorCode.RightDenied
        or DriveErrorCode.Unsupported;

    /// <summary>
    /// The shared runner host over the resolved settings. No agent-facing mount: the verbs only
    /// need the drive services, and the host mounts the internal credentials root by itself
    /// whenever a drive account is declared.
    /// </summary>
    private static IHost BuildHost(DriveCommandOptionsBase options)
    {
        var cwd = Path.GetFullPath(options.WorkingDirectoryOverride ?? Directory.GetCurrentDirectory());
        var settingsPath = RunnerSettings.ResolveSettingsPath(options.SettingsPath, cwd);
        if (settingsPath != null)
            Console.Error.WriteLine($"Using settings: {settingsPath}");

        return RunnerHost.Build(
            settingsPath,
            new RunnerMountPlan(),
            configureLogging: (_, logging) =>
            {
                // Standard output carries the verb's answer (`accounts --json` is parsed by
                // tools): every log line goes to standard error.
                logging.ClearProviders();
                logging.AddSimpleConsole(console => console.SingleLine = true);
                logging.Services.Configure<ConsoleLoggerOptions>(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
                logging.SetMinimumLevel(LogLevel.Warning);
            },
            configureServices: (context, services) => options.ConfigureTestServices?.Invoke(context, services));
    }
}
