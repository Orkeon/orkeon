using Orkeon.Constants.Protocol;
using Orkeon.Studio.Core.Drive;
using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Email;

/// <summary>
/// The event kinds of one account family's <c>login</c> and <c>check</c> verbs, under its verb.
/// The e-mail and the drive families speak the same protocol (<see cref="EmailEventKinds"/>,
/// <see cref="DriveEventKinds"/>) under their own prefix; only the names differ.
/// </summary>
/// <param name="Verb">The CLI verb of the family: <c>email</c>, <c>drive</c>.</param>
/// <param name="LoginDeviceCode">The step that carries a device code.</param>
/// <param name="LoginAuthorizationUrl">The step that carries the address of a browser sign-in.</param>
/// <param name="LoginRedirectRejected">The step that says a pasted address was rejected.</param>
/// <param name="LoginCompleted">The step that says the tokens are stored.</param>
/// <param name="CheckCompleted">The verdict of a check that connected.</param>
/// <param name="Error">The verdict of a verb that refused.</param>
internal sealed record AccountCliProtocol(
    string Verb,
    string LoginDeviceCode,
    string LoginAuthorizationUrl,
    string LoginRedirectRejected,
    string LoginCompleted,
    string CheckCompleted,
    string Error)
{
    /// <summary><c>orkeon email</c>.</summary>
    public static AccountCliProtocol Email { get; } = new(
        EmailArgumentsBuilder.Verb,
        EmailEventKinds.LoginDeviceCode,
        EmailEventKinds.LoginAuthorizationUrl,
        EmailEventKinds.LoginRedirectRejected,
        EmailEventKinds.LoginCompleted,
        EmailEventKinds.CheckCompleted,
        EmailEventKinds.Error);

    /// <summary><c>orkeon drive</c>.</summary>
    public static AccountCliProtocol Drive { get; } = new(
        DriveArgumentsBuilder.Verb,
        DriveEventKinds.LoginDeviceCode,
        DriveEventKinds.LoginAuthorizationUrl,
        DriveEventKinds.LoginRedirectRejected,
        DriveEventKinds.LoginCompleted,
        DriveEventKinds.CheckCompleted,
        DriveEventKinds.Error);
}

/// <summary>
/// How Studio follows the verbs an account family shares (STUDIO-69, STUDIO-70): a check read off
/// its verdict event, a sign-in followed step by step with its standard input piped, a sign-out,
/// and the failures typed rather than thrown. <see cref="EmailCliClient"/> and
/// <see cref="DriveCliClient"/> each hand it their argv and read their own account list.
/// </summary>
internal sealed class AccountCli
{
    private const string SettingsLinePrefix = "Using settings:";

    private readonly OrkeonProcessRunner _runner;
    private readonly AccountCliProtocol _protocol;

    public AccountCli(OrkeonProcessRunner runner, AccountCliProtocol protocol)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _protocol = protocol;
    }

    /// <summary>
    /// Runs a <c>check</c> argv: a real connection to the provider. Cancelling
    /// <paramref name="cancellationToken"/> stops the child. Never throws for what the process did.
    /// </summary>
    public async Task<EmailCheckOutcome> CheckAsync(IReadOnlyList<string> arguments, string verb, CancellationToken cancellationToken)
    {
        var (run, stdout, stderr) = await RunAsync(arguments, cancellationToken).ConfigureAwait(false);

        // The verdict is an event line (the counts, or the error with its code); a verb that
        // printed a sentence instead — an older CLI — is read as before.
        string? summary = null;
        string? code = null;
        string? message = null;
        foreach (var line in stdout)
        {
            if (!OrkeonEventParser.TryParse(line, out var orkeonEvent))
                continue;

            if (orkeonEvent!.Kind == _protocol.CheckCompleted)
            {
                summary = orkeonEvent.GetString("summary");
            }
            else if (orkeonEvent.Kind == _protocol.Error)
            {
                code = orkeonEvent.GetString("code");
                message = orkeonEvent.GetString("message");
            }
        }

        var refused = message is { Length: > 0 } ? message : Said(run, stderr, verb);
        return run.Outcome switch
        {
            RunOutcome.NotStarted => new EmailCheckOutcome(EmailCheckKind.Unavailable, run.Description),
            RunOutcome.Cancelled => new EmailCheckOutcome(EmailCheckKind.Cancelled, run.Description, run.ExitCode),
            RunOutcome.Success => new EmailCheckOutcome(
                EmailCheckKind.Reachable,
                summary is { Length: > 0 }
                    ? summary
                    : stdout.LastOrDefault(line => !string.IsNullOrWhiteSpace(line) && !OrkeonEventParser.TryParse(line, out _))?.Trim() ?? run.Description,
                run.ExitCode),
            RunOutcome.ScriptError => new EmailCheckOutcome(EmailCheckKind.OperatorFixable, refused, run.ExitCode, code),
            RunOutcome.RuntimeError => new EmailCheckOutcome(EmailCheckKind.ServerOrNetwork, refused, run.ExitCode, code),
            _ => new EmailCheckOutcome(EmailCheckKind.Unavailable, run.Description, run.ExitCode),
        };
    }

    /// <summary>
    /// Runs a <c>login --events jsonl</c> argv and follows it; see <see cref="EmailCliClient.LoginAsync"/>.
    /// The input is closed as soon as the verb said its last word, synchronously when
    /// <paramref name="cancellationToken"/> fires, and by the operating system should this process
    /// end without either. Never throws for what the process did.
    /// </summary>
    public async Task<EmailLoginStep> LoginAsync(
        IReadOnlyList<string> arguments,
        string verb,
        Action<EmailLoginStep> onStep,
        EmailLoginInput? input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onStep);
        var dialogue = input ?? new EmailLoginInput();
        var stderr = new List<string>();
        EmailLoginStep? ended = null;
        EmailCliFailure? refusal = null;

        ProcessRunResult run;
        using (cancellationToken.Register(dialogue.Close))
        {
            try
            {
                run = await _runner.RunAsync(
                    arguments,
                    onOutput: line =>
                    {
                        if (line.Channel == ProcessOutputChannel.StandardError)
                        {
                            stderr.Add(line.Text);
                            return;
                        }

                        if (!OrkeonEventParser.TryParse(line.Text, out var orkeonEvent))
                            return;

                        var kind = orkeonEvent!.Kind;
                        if (kind == _protocol.LoginDeviceCode && orkeonEvent.GetString("user_code") is { Length: > 0 } userCode)
                        {
                            onStep(new EmailLoginStep
                            {
                                Kind = EmailLoginStepKind.DeviceCode,
                                Address = orkeonEvent.GetString("verification_uri"),
                                UserCode = userCode,
                                ExpiresIn = orkeonEvent.GetInt64("expires_in") is { } seconds and > 0 ? TimeSpan.FromSeconds(seconds) : null,
                            });
                        }
                        else if (kind == _protocol.LoginAuthorizationUrl && orkeonEvent.GetString("authorization_uri") is { Length: > 0 } address)
                        {
                            onStep(new EmailLoginStep { Kind = EmailLoginStepKind.AuthorizationUrl, Address = address });
                        }
                        else if (kind == _protocol.LoginRedirectRejected)
                        {
                            // The verb still waits, and still reads: nothing is closed.
                            onStep(new EmailLoginStep { Kind = EmailLoginStepKind.RedirectRejected, Message = orkeonEvent.GetString("message") ?? "" });
                        }
                        else if (kind == _protocol.LoginCompleted)
                        {
                            ended = EmailLoginStep.Completed;
                            dialogue.Close();
                        }
                        else if (kind == _protocol.Error)
                        {
                            refusal = new EmailCliFailure(
                                EmailCliFailureKind.Refused,
                                orkeonEvent.GetString("message") ?? "",
                                Code: orkeonEvent.GetString("code"));
                            dialogue.Close();
                        }
                    },
                    onInputReady: dialogue.Attach,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Whatever ended the run, nothing reads that input any more.
                dialogue.Close();
            }
        }

        if (run.Outcome == RunOutcome.NotStarted)
            return EmailLoginStep.Failed(new EmailCliFailure(EmailCliFailureKind.EngineMissing, run.Description));

        if (ended is not null)
            return ended;

        if (refusal is not null)
            return EmailLoginStep.Failed(refusal with { ExitCode = run.ExitCode });

        return EmailLoginStep.Failed(run.Outcome switch
        {
            RunOutcome.Cancelled => new EmailCliFailure(EmailCliFailureKind.Cancelled, run.Description, run.ExitCode),
            RunOutcome.Success => new EmailCliFailure(
                EmailCliFailureKind.Unreadable,
                $"orkeon {_protocol.Verb} {verb} ended without an {_protocol.LoginCompleted} event — is it a version without --events?",
                run.ExitCode),
            _ => new EmailCliFailure(EmailCliFailureKind.Stopped, Said(run, stderr, verb), run.ExitCode),
        });
    }

    /// <summary>Runs a <c>logout</c> argv: null when the verb ended well, why it did not otherwise.</summary>
    public async Task<EmailCliFailure?> LogoutAsync(IReadOnlyList<string> arguments, string verb, CancellationToken cancellationToken)
    {
        var (run, _, stderr) = await RunAsync(arguments, cancellationToken).ConfigureAwait(false);
        return FailureOf(run, stderr, verb);
    }

    /// <summary>Runs <paramref name="arguments"/>, standard output and standard error kept apart.</summary>
    public async Task<(ProcessRunResult Run, List<string> Stdout, List<string> Stderr)> RunAsync(
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var stdout = new List<string>();
        var stderr = new List<string>();
        var run = await _runner.RunAsync(
            arguments,
            onOutput: line => (line.Channel == ProcessOutputChannel.StandardOutput ? stdout : stderr).Add(line.Text),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return (run, stdout, stderr);
    }

    /// <summary>Why the run carries no answer to read; null when it ended on exit code 0.</summary>
    public EmailCliFailure? FailureOf(ProcessRunResult run, List<string> stderr, string verb) => run.Outcome switch
    {
        RunOutcome.Success => null,
        RunOutcome.NotStarted => new EmailCliFailure(EmailCliFailureKind.EngineMissing, run.Description),
        RunOutcome.Cancelled => new EmailCliFailure(EmailCliFailureKind.Cancelled, run.Description, run.ExitCode),
        _ => new EmailCliFailure(EmailCliFailureKind.Stopped, Said(run, stderr, verb), run.ExitCode),
    };

    /// <summary>
    /// What a verb that failed said: its own <c>orkeon &lt;family&gt; &lt;verb&gt;: …</c> line without
    /// that prefix, else the last thing on standard error that is not the settings line, else what
    /// the exit code means.
    /// </summary>
    private string Said(ProcessRunResult run, List<string> stderr, string verb)
    {
        var prefix = $"orkeon {_protocol.Verb} {verb}: ";
        var lines = stderr
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith(SettingsLinePrefix, StringComparison.Ordinal))
            .ToList();

        if (lines.LastOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal)) is { } message
            && message[prefix.Length..].Trim() is { Length: > 0 } text)
        {
            return text;
        }

        return lines.Count > 0 ? lines[^1] : run.Description;
    }
}
