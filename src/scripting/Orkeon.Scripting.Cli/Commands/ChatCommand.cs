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
using Orkeon.Tools.Chat;
using Orkeon.Tools.Chat.Administration;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Options shared by the <c>orkeon chat</c> verbs.</summary>
internal abstract class ChatCommandOptionsBase : CliOptions
{
    /// <summary>Path to appsettings.json holding <c>Orkeon:Tools:Chat</c>.</summary>
    [Option('s', "settings", Required = false,
        HelpText = "Path to appsettings.json (defaults to the current directory's resolution chain, like `orkeon run`).")]
    public string? SettingsPath { get; set; }

    /// <summary>Test seam: overrides the current directory the settings chain is anchored at.</summary>
    internal string? WorkingDirectoryOverride { get; set; }

    /// <summary>Test seam: services registered after the runner host's, so doubles replace the real ones.</summary>
    internal Action<HostBuilderContext, IServiceCollection>? ConfigureTestServices { get; set; }
}

/// <summary>Parsed options of <c>orkeon chat accounts</c>.</summary>
[Verb("accounts", HelpText = "List the configured chat accounts (Microsoft Teams), their rights, allowed places and whether each is ready (no network).")]
internal sealed class ChatAccountsCommandOptions : ChatCommandOptionsBase
{
    /// <summary>Writes the list as JSON.</summary>
    [Option("json", Required = false, Default = false, HelpText = "Write the list as JSON.")]
    public bool Json { get; set; }
}

/// <summary>Parsed options of <c>orkeon chat login</c>.</summary>
[Verb("login", HelpText = "Sign a chat account in (in the browser, or with a device code) and store its tokens.")]
internal sealed class ChatLoginCommandOptions : ChatCommandOptionsBase
{
    /// <summary>The account to sign in.</summary>
    [Value(0, Required = true, MetaName = "account", HelpText = "The account name, as declared under Orkeon:Tools:Chat:Accounts.")]
    public string Account { get; set; } = string.Empty;

    /// <summary>Signs in with a code typed in any browser, instead of a browser on this machine.</summary>
    [Option("device-code", Required = false, Default = false,
        HelpText = "Sign in with a device code (a container, a remote shell); some organizations block that flow. The default opens a page in a browser on this machine.")]
    public bool DeviceCode { get; set; }

    /// <summary><c>--events jsonl</c>: the sign-in as protocol lines on stdout, for a program that drives it.</summary>
    [Option("events", Required = false,
        HelpText = "jsonl: write each step of the sign-in as one event line on stdout, read the pasted redirect address on stdin, and abort when stdin closes (the way a client that drives the CLI uses it).")]
    public string? Events { get; set; }

    /// <summary>Test seam: the terminal interaction.</summary>
    internal IOAuthLoginInteraction? Interaction { get; set; }
}

/// <summary>Parsed options of <c>orkeon chat logout</c>.</summary>
[Verb("logout", HelpText = "Delete the stored OAuth tokens of a chat account, and say where its access is removed.")]
internal sealed class ChatLogoutCommandOptions : ChatCommandOptionsBase
{
    /// <summary>The account to sign out.</summary>
    [Value(0, Required = true, MetaName = "account", HelpText = "The account name.")]
    public string Account { get; set; } = string.Empty;
}

/// <summary>Parsed options of <c>orkeon chat check</c>.</summary>
[Verb("check", HelpText = "Connect to a chat account and tell, right by right, what it can do and what is missing.")]
internal sealed class ChatCheckCommandOptions : ChatCommandOptionsBase
{
    /// <summary>The account to check; the default one when empty.</summary>
    [Value(0, Required = false, MetaName = "account", HelpText = "The account name; by default the configured DefaultAccount, or the only account declared.")]
    public string Account { get; set; } = string.Empty;

    /// <summary><c>--events jsonl</c>: the verdict as one event line on stdout, for a program that drives the check.</summary>
    [Option("events", Required = false,
        HelpText = "jsonl: write the verdict as one event line on stdout — what each right can do, or the error with its code (the way a client that drives the CLI uses it).")]
    public string? Events { get; set; }
}

/// <summary>
/// <c>orkeon chat accounts | login | logout | check</c> — the operator's side of the chat tool
/// family (#446): what is configured, the one interactive step every account needs, and a
/// connection check. Builds the shared runner host over the resolved settings file, so the OAuth
/// tokens land where every run will look for them.
/// </summary>
internal static class ChatCommand
{
    private const string EventsFormat = "jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Parses <paramref name="args"/> (already stripped of the leading <c>chat</c>) and dispatches to a verb.</summary>
    public static async Task<int> DispatchAsync(IRunnerConsole console, string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return await CliGrammar.DispatchAsync(console, "chat", args, [typeof(ChatAccountsCommandOptions), typeof(ChatLoginCommandOptions), typeof(ChatLogoutCommandOptions), typeof(ChatCheckCommandOptions)], (console, options) => options switch
        {
            ChatAccountsCommandOptions o => ExecuteAccountsAsync(console, o),
            ChatLoginCommandOptions o => ExecuteLoginAsync(console, o),
            ChatLogoutCommandOptions o => ExecuteLogoutAsync(console, o),
            ChatCheckCommandOptions o => ExecuteCheckAsync(console, o),
            _ => Task.FromResult(Program.ExitScriptError),
        }).ConfigureAwait(false);
    }

    /// <summary>Lists the accounts.</summary>
    public static Task<int> ExecuteAccountsAsync(IRunnerConsole console, ChatAccountsCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(options);
        return GuardedAsync(console, "accounts", options, async (administration, ct) =>
        {
            var accounts = await administration.ListAsync(ct).ConfigureAwait(false);
            if (options.Json)
            {
                await console.Output.WriteLineAsync(JsonSerializer.Serialize(accounts, JsonOptions)).ConfigureAwait(false);
                return Program.ExitOk;
            }

            if (accounts.Count == 0)
            {
                await console.Output.WriteLineAsync(
                    "No chat account is configured. Declare one under Orkeon:Tools:Chat:Accounts in the settings file (see docs/guides/chat.md).")
                    .ConfigureAwait(false);
                return Program.ExitOk;
            }

            await console.Output.WriteAsync(RenderAccounts(accounts)).ConfigureAwait(false);
            return Program.ExitOk;
        });
    }

    /// <summary>
    /// Signs an account in. With <c>--events jsonl</c> the steps are event lines on standard
    /// output, the pasted redirect address is read on standard input, and the end of that input
    /// aborts the sign-in — as <c>orkeon email login</c> does.
    /// </summary>
    public static async Task<int> ExecuteLoginAsync(IRunnerConsole console, ChatLoginCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(options);
        if (await RefuseFormatAsync(console.ErrorOutput, "login", options.Events).ConfigureAwait(false))
            return Program.ExitScriptError;

        var events = options.Events is null ? null : new ChatEventWriter(console.Output);
        return await GuardedAsync(console, "login", options, async (administration, ct) =>
        {
            if (events is null)
            {
                var interaction = options.Interaction ?? new ConsoleOAuthLoginInteraction("chat", console);
                await administration.LoginAsync(options.Account, options.DeviceCode, interaction, ct).ConfigureAwait(false);
                await console.Output.WriteLineAsync($"Signed in: the tokens of chat account '{options.Account}' are stored. Agents can use it now.")
                    .ConfigureAwait(false);
                return Program.ExitOk;
            }

            using var driver = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var dialogue = new EventOAuthLoginInteraction(events, console.Input, onInputClosed: () =>
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
                await administration.LoginAsync(options.Account, options.DeviceCode, dialogue, driver.Token).ConfigureAwait(false);
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
    public static Task<int> ExecuteLogoutAsync(IRunnerConsole console, ChatLogoutCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(options);
        return GuardedAsync(console, "logout", options, async (administration, ct) =>
        {
            var result = await administration.LogoutAsync(options.Account, ct).ConfigureAwait(false);
            await console.Output.WriteLineAsync(RenderLogout(options.Account, result)).ConfigureAwait(false);
            return Program.ExitOk;
        });
    }

    /// <summary>Checks that an account connects; with <c>--events jsonl</c> the verdict is one event line.</summary>
    public static async Task<int> ExecuteCheckAsync(IRunnerConsole console, ChatCheckCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(options);
        if (await RefuseFormatAsync(console.ErrorOutput, "check", options.Events).ConfigureAwait(false))
            return Program.ExitScriptError;

        var events = options.Events is null ? null : new ChatEventWriter(console.Output);
        return await GuardedAsync(console, "check", options, async (administration, ct) =>
        {
            var result = await administration.CheckAsync(options.Account, ct).ConfigureAwait(false);
            var summary = RenderCheck(result);
            if (events is null)
                await console.Output.WriteLineAsync(summary).ConfigureAwait(false);
            else
                events.CheckCompleted(result, summary);

            // What is missing is a consent or a setting: the operator fixes it.
            return result.Ready ? Program.ExitOk : Program.ExitScriptError;
        }, events).ConfigureAwait(false);
    }

    /// <summary>Says, and answers true, when <paramref name="format"/> is an events format the verbs do not write.</summary>
    private static async Task<bool> RefuseFormatAsync(TextWriter error, string verb, string? format)
    {
        if (format is null || string.Equals(format, EventsFormat, StringComparison.OrdinalIgnoreCase))
            return false;
        await error.WriteLineAsync($"orkeon chat {verb}: unsupported --events format '{format}' — the only one is {EventsFormat}.").ConfigureAwait(false);
        return true;
    }

    /// <summary>What became of a sign-out: the tokens, then whether the provider still honours them.</summary>
    internal static string RenderLogout(string account, ChatLogoutResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Deleted)
            return $"Chat account '{account}' had no stored tokens.";

        var revoked = result.Revoked ? " and revoked" : string.Empty;
        var notice = result.Notice is null ? string.Empty : Environment.NewLine + result.Notice.TrimEnd('.') + ".";
        return $"Signed out: the tokens of chat account '{account}' are deleted{revoked}.{notice}";
    }

    /// <summary>The text of a check that connected: the signed-in user when it may be read, then each right.</summary>
    internal static string RenderCheck(ChatCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var text = new StringBuilder();
        text.Append("Chat account '").Append(result.Account).Append("' is reachable");
        if (result.User is { } user)
            text.Append(" as ").Append(user);
        text.Append('.');
        foreach (var right in result.Rights)
        {
            text.AppendLine().Append("  ").Append(right.Right).Append(": ").Append(right.Ready ? "ready" : "NOT READY");
            if (right.Missing.Count > 0)
                text.Append(" - ").AppendJoin("; ", right.Missing);
        }

        return text.ToString();
    }

    /// <summary>The human listing of <paramref name="accounts"/>.</summary>
    internal static string RenderAccounts(IReadOnlyList<ChatAccountStatus> accounts)
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
                text.Append("  ").Append(account.Provider).Append(" · cloud ").AppendLine(account.Cloud);
                text.Append("  rights: ").AppendLine(account.Rights);
                text.Append("  allowed places: ")
                    .Append(CultureInfo.InvariantCulture, $"{account.ReadPlaces} to read, {account.SendPlaces} to post in").AppendLine();
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
        IRunnerConsole console,
        string verb,
        ChatCommandOptionsBase options,
        Func<ChatAccountAdministration, CancellationToken, Task<int>> body,
        ChatEventWriter? events = null)
    {
        using var cts = new CancellationTokenSource();
        using var interrupt = console.OnInterrupt(() =>
        {
            cts.Cancel();
            return true;
        });

        try
        {
            using var host = BuildHost(console, options);
            var administration = host.Services.GetRequiredService<ChatAccountAdministration>();
            return await body(administration, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Only the operator's Ctrl+C: a cancellation nobody asked for is a failure, below.
            return Program.ExitCancelled;
        }
        catch (ChatToolException ex)
        {
            await FailAsync(console.ErrorOutput, verb, events, ex.Code.ToString(), ex.Message).ConfigureAwait(false);
            return IsOperatorFixable(ex.Code) ? Program.ExitScriptError : Program.ExitRuntimeError;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FileNotFoundException or DirectoryNotFoundException)
        {
            await FailAsync(console.ErrorOutput, verb, events, nameof(ChatErrorCode.InvalidConfiguration), ex.Message).ConfigureAwait(false);
            return Program.ExitScriptError;
        }
        catch (Exception ex)
        {
            await FailAsync(console.ErrorOutput, verb, events, ChatEventWriter.UnexpectedCode, $"unexpected error [{ex.GetType().FullName}]: {ex.Message}").ConfigureAwait(false);
            if (RunnerEnvironment.DebugDiagnostics)
                await console.ErrorOutput.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
            return Program.ExitRuntimeError;
        }
    }

    /// <summary>Says why the verb failed: an <c>error</c> event when a program drives it, one line on stderr otherwise.</summary>
    private static Task FailAsync(TextWriter error, string verb, ChatEventWriter? events, string code, string message)
    {
        if (events is null)
            return error.WriteLineAsync($"orkeon chat {verb}: {message}");

        events.Error(code, message);
        return Task.CompletedTask;
    }

    private static bool IsOperatorFixable(ChatErrorCode code) => code is ChatErrorCode.NotConfigured
        or ChatErrorCode.UnknownAccount
        or ChatErrorCode.InvalidConfiguration
        or ChatErrorCode.RightMissing
        or ChatErrorCode.PlaceNotAllowed
        or ChatErrorCode.ConsentMissing
        or ChatErrorCode.SignInRequired
        or ChatErrorCode.InvalidRequest
        or ChatErrorCode.NotSupportedByAccount;

    /// <summary>
    /// The shared runner host over the resolved settings. No agent-facing mount: the verbs only
    /// need the chat services, and the host mounts the internal credentials root by itself
    /// whenever a chat account is declared.
    /// </summary>
    private static IHost BuildHost(IRunnerConsole console, ChatCommandOptionsBase options)
    {
        var cwd = Path.GetFullPath(options.WorkingDirectoryOverride ?? Directory.GetCurrentDirectory());
        var settingsPath = RunnerSettings.ResolveSettingsPath(options.SettingsPath, cwd, console.ErrorOutput);
        if (settingsPath != null)
            console.ErrorOutput.WriteLine($"Using settings: {settingsPath}");

        return RunnerHost.Build(
            settingsPath,
            new RunnerMountPlan(),
            configureLogging: (_, logging) =>
            {
                // Standard output carries the verb's answer (`accounts --json` is parsed by
                // tools): every log line goes to standard error.
                logging.ClearProviders();
                logging.AddSimpleConsole(o => o.SingleLine = true);
                logging.Services.Configure<ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
                logging.SetMinimumLevel(LogLevel.Warning);
            },
            configureServices: (context, services) =>
            {
                services.AddSingleton(console);
                // The provider modules of the tool: a settings file `orkeon run` accepts is accepted here (#472).
                CliLlmProviderModules.AddTo(services);
                options.ConfigureTestServices?.Invoke(context, services);
            });
    }
}
