using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Orkeon.Hosting;
using Orkeon.Tools.Email;
using Orkeon.Tools.Email.Administration;

namespace Orkeon.Scripting.Cli.Commands;

/// <summary>Options shared by the <c>orkeon email</c> verbs.</summary>
internal abstract class EmailCommandOptionsBase
{
    /// <summary>Path to appsettings.json holding <c>Orkeon:Tools:Email</c>.</summary>
    [Option('s', "settings", Required = false,
        HelpText = "Path to appsettings.json (defaults to the current directory's resolution chain, like `orkeon run`).")]
    public string? SettingsPath { get; set; }

    /// <summary>Test seam: overrides the current directory the settings chain is anchored at.</summary>
    internal string? WorkingDirectoryOverride { get; set; }

    /// <summary>Test seam: services registered after the runner host's, so doubles replace the real ones.</summary>
    internal Action<HostBuilderContext, IServiceCollection>? ConfigureTestServices { get; set; }
}

/// <summary>Parsed options of <c>orkeon email accounts</c>.</summary>
[Verb("accounts", HelpText = "List the configured e-mail accounts, their rights and whether each is ready (no network).")]
internal sealed class EmailAccountsCommandOptions : EmailCommandOptionsBase
{
    /// <summary>Writes the list as JSON.</summary>
    [Option("json", Required = false, Default = false, HelpText = "Write the list as JSON.")]
    public bool Json { get; set; }
}

/// <summary>Parsed options of <c>orkeon email login</c>.</summary>
[Verb("login", HelpText = "Sign an OAuth2 e-mail account in (Microsoft device code, or Google in the browser) and store its tokens.")]
internal sealed class EmailLoginCommandOptions : EmailCommandOptionsBase
{
    /// <summary>The account to sign in.</summary>
    [Value(0, Required = true, MetaName = "account", HelpText = "The account name, as declared under Orkeon:Tools:Email:Accounts.")]
    public string Account { get; set; } = string.Empty;

    /// <summary><c>--events jsonl</c>: the sign-in as protocol lines on stdout, for a program that drives it.</summary>
    [Option("events", Required = false,
        HelpText = "jsonl: write each step of the sign-in as one event line on stdout, read the pasted redirect address on stdin, and abort when stdin closes (the way Orkeon Studio drives it).")]
    public string? Events { get; set; }

    /// <summary>Test seam: the terminal interaction.</summary>
    internal IEmailLoginInteraction? Interaction { get; set; }
}

/// <summary>Parsed options of <c>orkeon email logout</c>.</summary>
[Verb("logout", HelpText = "Forget the stored OAuth tokens of an e-mail account.")]
internal sealed class EmailLogoutCommandOptions : EmailCommandOptionsBase
{
    /// <summary>The account to sign out.</summary>
    [Value(0, Required = true, MetaName = "account", HelpText = "The account name.")]
    public string Account { get; set; } = string.Empty;
}

/// <summary>Parsed options of <c>orkeon email check</c>.</summary>
[Verb("check", HelpText = "Connect to an e-mail account, authenticate and list its folders.")]
internal sealed class EmailCheckCommandOptions : EmailCommandOptionsBase
{
    /// <summary>The account to check.</summary>
    [Value(0, Required = true, MetaName = "account", HelpText = "The account name.")]
    public string Account { get; set; } = string.Empty;
}

/// <summary>
/// <c>orkeon email accounts | login | logout | check</c> — the operator's side of the e-mail
/// tool family (MAIL-05): what is configured, the one interactive step an OAuth account needs,
/// and a connection check. Builds the shared runner host over the resolved settings file, so
/// the OAuth tokens land where every run will look for them.
/// </summary>
internal static class EmailCommand
{
    private const string EventsFormat = "jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Parses <paramref name="args"/> (already stripped of the leading <c>email</c>) and dispatches to a verb.</summary>
    public static async Task<int> DispatchAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        using var parser = new Parser(s =>
        {
            s.HelpWriter = Console.Out;
            s.CaseInsensitiveEnumValues = true;
        });

        return await parser.ParseArguments<EmailAccountsCommandOptions, EmailLoginCommandOptions, EmailLogoutCommandOptions, EmailCheckCommandOptions>(args)
            .MapResult(
                (EmailAccountsCommandOptions o) => ExecuteAccountsAsync(o),
                (EmailLoginCommandOptions o) => ExecuteLoginAsync(o),
                (EmailLogoutCommandOptions o) => ExecuteLogoutAsync(o),
                (EmailCheckCommandOptions o) => ExecuteCheckAsync(o),
                _ => Task.FromResult(Program.ExitScriptError))
            .ConfigureAwait(false);
    }

    /// <summary>Lists the accounts.</summary>
    public static Task<int> ExecuteAccountsAsync(EmailAccountsCommandOptions options)
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
                    "No e-mail account is configured. Declare one under Orkeon:Tools:Email:Accounts in the settings file (see docs/guides/email.md).")
                    .ConfigureAwait(false);
                return Program.ExitOk;
            }

            await Console.Out.WriteAsync(RenderAccounts(accounts)).ConfigureAwait(false);
            return Program.ExitOk;
        });
    }

    /// <summary>
    /// Signs an account in. With <c>--events jsonl</c> (STUDIO-70) the steps are event lines on
    /// standard output for a program that drives the verb, and that program's hold on it is the
    /// verb's standard input: the pasted redirect address is read there, and its end aborts the
    /// sign-in — the loopback wait has no deadline, so a driver that goes away must not leave it
    /// listening.
    /// </summary>
    public static async Task<int> ExecuteLoginAsync(EmailLoginCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Events is { } format && !string.Equals(format, EventsFormat, StringComparison.OrdinalIgnoreCase))
        {
            await Console.Error.WriteLineAsync(
                $"orkeon email login: unsupported --events format '{format}' — the only one is {EventsFormat}.")
                .ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        var events = options.Events is null ? null : new EmailEventWriter(Console.Out);
        return await GuardedAsync("login", options, async (administration, ct) =>
        {
            if (events is null)
            {
                var interaction = options.Interaction ?? new ConsoleLoginInteraction();
                await administration.LoginAsync(options.Account, interaction, ct).ConfigureAwait(false);
                await Console.Out.WriteLineAsync($"Signed in: the tokens of e-mail account '{options.Account}' are stored. Agents can use it now.")
                    .ConfigureAwait(false);
                return Program.ExitOk;
            }

            using var driver = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var dialogue = new EventLoginInteraction(events, Console.In, onInputClosed: () =>
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
    public static Task<int> ExecuteLogoutAsync(EmailLogoutCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GuardedAsync("logout", options, async (administration, ct) =>
        {
            var removed = await administration.LogoutAsync(options.Account, ct).ConfigureAwait(false);
            await Console.Out.WriteLineAsync(removed
                    ? $"Signed out: the tokens of e-mail account '{options.Account}' are deleted."
                    : $"E-mail account '{options.Account}' had no stored tokens.")
                .ConfigureAwait(false);
            return Program.ExitOk;
        });
    }

    /// <summary>Checks that an account connects.</summary>
    public static Task<int> ExecuteCheckAsync(EmailCheckCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GuardedAsync("check", options, async (administration, ct) =>
        {
            var result = await administration.CheckAsync(options.Account, ct).ConfigureAwait(false);
            var inbox = string.Empty;
            if (result.InboxTotal is { } total)
            {
                var unreadPart = result.InboxUnread is { } unread
                    ? string.Create(CultureInfo.InvariantCulture, $", {unread} unread")
                    : string.Empty;
                inbox = string.Create(CultureInfo.InvariantCulture, $", inbox {total} message(s){unreadPart}");
            }
            await Console.Out.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                    $"E-mail account '{result.Account}' is reachable: {result.Folders} folder(s){inbox}."))
                .ConfigureAwait(false);
            return Program.ExitOk;
        });
    }

    /// <summary>The human listing of <paramref name="accounts"/>.</summary>
    internal static string RenderAccounts(IReadOnlyList<EmailAccountStatus> accounts)
    {
        var text = new StringBuilder();
        foreach (var account in accounts)
        {
            text.Append(account.Name);
            if (account.IsDefault)
                text.Append(" (default)");
            if (account.Address is not null)
                text.Append(" — ").Append(account.Address);
            text.AppendLine();

            if (account.Reads is not null)
            {
                text.Append("  ").Append(account.Provider)
                    .Append(" · reads over ").Append(account.Reads)
                    .Append(" · sends ").Append(account.Sends is null ? "nothing" : "over " + account.Sends)
                    .Append(" · signs in with ").Append(account.Auth)
                    .AppendLine();
                text.Append("  rights: ").AppendLine(account.Rights);
            }

            text.Append("  ").AppendLine(account.Ready ? "ready" : "NOT READY: " + account.Problem);
        }

        return text.ToString();
    }

    /// <summary>
    /// Top-level fault barrier: Ctrl+C → 130; what the operator fixes (configuration, unknown
    /// account, missing variable, sign-in needed) → 1; what the server or the network did → 2.
    /// With <paramref name="events"/> the failure is an <c>error</c> line on stdout — the channel
    /// the driver reads — instead of a sentence on stderr; the exit code is the same.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Top-level CLI fault barrier: unexpected failures are converted to a runtime-error exit code so the tool reports cleanly instead of crashing with a stack trace.")]
    private static async Task<int> GuardedAsync(
        string verb,
        EmailCommandOptionsBase options,
        Func<EmailAccountAdministration, CancellationToken, Task<int>> body,
        EmailEventWriter? events = null)
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
            var administration = host.Services.GetRequiredService<EmailAccountAdministration>();
            return await body(administration, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Only the operator's Ctrl+C: a cancellation nobody asked for is a failure, below.
            return Program.ExitCancelled;
        }
        catch (EmailToolException ex)
        {
            await FailAsync(verb, events, ex.Code.ToString(), ex.Message).ConfigureAwait(false);
            return IsOperatorFixable(ex.Code) ? Program.ExitScriptError : Program.ExitRuntimeError;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FileNotFoundException or DirectoryNotFoundException)
        {
            await FailAsync(verb, events, nameof(EmailErrorCode.InvalidConfiguration), ex.Message).ConfigureAwait(false);
            return Program.ExitScriptError;
        }
        catch (Exception ex)
        {
            await FailAsync(verb, events, EmailEventWriter.UnexpectedCode, $"unexpected error [{ex.GetType().FullName}]: {ex.Message}").ConfigureAwait(false);
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
    private static Task FailAsync(string verb, EmailEventWriter? events, string code, string message)
    {
        if (events is null)
            return Console.Error.WriteLineAsync($"orkeon email {verb}: {message}");

        events.Error(code, message);
        return Task.CompletedTask;
    }

    private static bool IsOperatorFixable(EmailErrorCode code) => code is EmailErrorCode.NotConfigured
        or EmailErrorCode.UnknownAccount
        or EmailErrorCode.InvalidConfiguration
        or EmailErrorCode.CredentialMissing
        or EmailErrorCode.LoginRequired
        or EmailErrorCode.InvalidRequest
        or EmailErrorCode.RightDenied
        or EmailErrorCode.Unsupported;

    /// <summary>
    /// The shared runner host over the resolved settings. No agent-facing mount: the verbs only
    /// need the e-mail services, and the host mounts the internal credentials root by itself
    /// whenever an OAuth account is declared.
    /// </summary>
    private static IHost BuildHost(EmailCommandOptionsBase options)
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

    /// <summary>The terminal side of an interactive sign-in.</summary>
    private sealed class ConsoleLoginInteraction : IEmailLoginInteraction
    {
        public Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, TimeSpan expiresIn, CancellationToken cancellationToken) =>
            Console.Out.WriteLineAsync(
                $"To sign e-mail account '{account}' in, open {verificationUri} in a browser and enter the code {userCode}.{Environment.NewLine}Waiting for the sign-in to complete (Ctrl+C to abort)…");

        public Task ShowAuthorizationUrlAsync(string account, Uri authorizationUri, CancellationToken cancellationToken) =>
            Console.Out.WriteLineAsync(
                $"To sign e-mail account '{account}' in, open this address in a browser:{Environment.NewLine}{Environment.NewLine}  {authorizationUri}{Environment.NewLine}{Environment.NewLine}" +
                "If the browser cannot reach this machine (WSL, a container, a remote shell), paste here the address it ends on (http://127.0.0.1:…) and press Enter.");

        public async Task<string?> ReadRedirectAsync(CancellationToken cancellationToken)
        {
            var line = await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            return line;
        }

        public Task ShowRedirectRejectedAsync(string account, string reason, CancellationToken cancellationToken) =>
            Console.Out.WriteLineAsync(
                $"That is not the address the browser ended on: {reason} Paste the complete address (http://127.0.0.1:…) and press Enter.");
    }

    /// <summary>
    /// The sign-in a program drives (STUDIO-70): each step is an event line, and the redirect
    /// address is whatever line the driver writes on standard input — a line that is not one is
    /// answered by an event too, never by silence. That input is read from the start, on a thread
    /// of its own — a read of a redirected input blocks its caller —, so that its end is noticed
    /// in the device sign-in too, where nothing is ever pasted.
    /// </summary>
    private sealed class EventLoginInteraction : IEmailLoginInteraction
    {
        private readonly EmailEventWriter _events;
        private readonly Channel<string> _pasted = Channel.CreateUnbounded<string>();

        public EventLoginInteraction(EmailEventWriter events, TextReader input, Action onInputClosed)
        {
            _events = events;
            _ = Task.Factory.StartNew(
                () => Pump(input, onInputClosed), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, TimeSpan expiresIn, CancellationToken cancellationToken)
        {
            _events.LoginDeviceCode(verificationUri, userCode, expiresIn);
            return Task.CompletedTask;
        }

        public Task ShowAuthorizationUrlAsync(string account, Uri authorizationUri, CancellationToken cancellationToken)
        {
            _events.LoginAuthorizationUrl(authorizationUri);
            return Task.CompletedTask;
        }

        public async Task<string?> ReadRedirectAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _pasted.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        public Task ShowRedirectRejectedAsync(string account, string reason, CancellationToken cancellationToken)
        {
            _events.LoginRedirectRejected(reason);
            return Task.CompletedTask;
        }

        private void Pump(TextReader input, Action onInputClosed)
        {
            try
            {
                while (input.ReadLine() is { } line)
                    _pasted.Writer.TryWrite(line);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The input broke rather than ended: the driver is gone all the same.
            }

            _pasted.Writer.TryComplete();
            onInputClosed();
        }
    }
}
