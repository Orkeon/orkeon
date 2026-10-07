using System.Text.Json;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Email;

/// <summary>
/// The <c>orkeon email</c> argv Studio sends (STUDIO-69). Both verbs always name the settings
/// file: without <c>--settings</c> the verb resolves its chain from the working directory of
/// Studio and may read another file than the one the screen shows. The drift pin is
/// <c>EmailCliClientTests</c>' golden argv.
/// </summary>
public static class EmailArgumentsBuilder
{
    /// <summary>The CLI verb.</summary>
    public const string Verb = "email";

    /// <summary>The sub-verb that lists the accounts.</summary>
    public const string AccountsVerb = "accounts";

    /// <summary>The sub-verb that connects to one account.</summary>
    public const string CheckVerb = "check";

    /// <summary><c>email accounts --json --settings &lt;file&gt;</c>: every account and whether it is ready, no network.</summary>
    public static IReadOnlyList<string> BuildAccounts(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        return [Verb, AccountsVerb, "--json", "--settings", settingsPath];
    }

    /// <summary><c>email check &lt;account&gt; --settings &lt;file&gt;</c>: a real connection, sign-in and folder listing.</summary>
    public static IReadOnlyList<string> BuildCheck(string account, string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        return [Verb, CheckVerb, account, "--settings", settingsPath];
    }
}

/// <summary>What stood between Studio and the list of accounts.</summary>
public enum EmailCliFailureKind
{
    /// <summary>No <c>orkeon</c> binary on this machine — the locator's words are the reason.</summary>
    EngineMissing,

    /// <summary>The verb ended on a non-zero exit code: what it printed on standard error is the reason.</summary>
    Stopped,

    /// <summary>The verb ended well and its standard output is no JSON array of accounts.</summary>
    Unreadable,

    /// <summary>The caller's token fired before the verb ended: the child was stopped.</summary>
    Cancelled,
}

/// <summary>Why no list came back, in the CLI's own words — never translated.</summary>
/// <param name="Kind">The family.</param>
/// <param name="Reason">The raw technical text.</param>
/// <param name="ExitCode">The child's exit code, when it ran and ended.</param>
public sealed record EmailCliFailure(EmailCliFailureKind Kind, string Reason, int? ExitCode = null);

/// <summary>
/// One account as <c>orkeon email accounts --json</c> describes it — the fields Studio shows, out
/// of the ten the verb writes. Nothing here is computed by Studio: whether an account is ready
/// depends on the environment and the token store of the <c>orkeon</c> process.
/// </summary>
public sealed record EmailAccountState
{
    /// <summary>What the verb writes as the provider of an account the engine set aside.</summary>
    public const string SetAsideProvider = "?";

    /// <summary>The account name, as the engine lists it.</summary>
    public required string Name { get; init; }

    /// <summary>Whether it has everything it needs to connect — checked by the engine without network.</summary>
    public bool Ready { get; init; }

    /// <summary>The engine's sentence on what to fix, as printed, when it is not ready.</summary>
    public string? Problem { get; init; }

    /// <summary>The preset, or <see cref="SetAsideProvider"/>.</summary>
    public string? Provider { get; init; }

    /// <summary>How it reads mail (<c>Imap</c>, <c>Pop3</c>, <c>Graph</c>).</summary>
    public string? Reads { get; init; }

    /// <summary>How it sends mail (<c>Smtp</c>, <c>Graph</c>), or null.</summary>
    public string? Sends { get; init; }

    /// <summary>How it signs in (<c>Password</c>, <c>OAuth2</c>).</summary>
    public string? Auth { get; init; }

    /// <summary>Whether it is the account a call without <c>account</c> uses.</summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// Whether the engine set the account aside: its declaration is refused, so no run will use
    /// it. Read off the marker the verb writes, never out of <see cref="Problem"/>.
    /// </summary>
    public bool IsSetAside => string.Equals(Provider, SetAsideProvider, StringComparison.Ordinal);
}

/// <summary>The accounts of a settings file, or why they could not be listed.</summary>
public sealed record EmailAccountsResult
{
    /// <summary>The accounts, in the engine's order — sorted by name, not the order of the file.</summary>
    public IReadOnlyList<EmailAccountState> Accounts { get; init; } = [];

    /// <summary>Why no list came back; null on success.</summary>
    public EmailCliFailure? Failure { get; init; }

    /// <summary>The account named <paramref name="name"/>, compared without regard to case as the engine does; null when the list has none.</summary>
    public EmailAccountState? Find(string name) =>
        Accounts.FirstOrDefault(account => string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>How a connection check ended. The exit code of the verb is all that tells the families apart.</summary>
public enum EmailCheckKind
{
    /// <summary>Exit code 0: the account connected, signed in and listed its folders.</summary>
    Reachable,

    /// <summary>Exit code 1: a setting, a secret or a sign-in is missing — fixed on this machine.</summary>
    OperatorFixable,

    /// <summary>Exit code 2: the server refused or the network failed; the sentence says which.</summary>
    ServerOrNetwork,

    /// <summary>No verdict on the account: the CLI is missing, or ended in a way the verb does not document.</summary>
    Unavailable,

    /// <summary>The caller's token fired: the child was stopped before it answered.</summary>
    Cancelled,
}

/// <summary>The verdict of <c>orkeon email check</c> and the sentence that came with it.</summary>
/// <param name="Kind">The family, from the exit code.</param>
/// <param name="Sentence">
/// The engine's sentence as printed, in English — the answer on success, the message without the
/// verb's own prefix on a refusal. Shown as is: nothing is read out of it.
/// </param>
/// <param name="ExitCode">The child's exit code, when it ran and ended.</param>
public sealed record EmailCheckOutcome(EmailCheckKind Kind, string Sentence, int? ExitCode = null);

/// <summary>
/// The Studio side of <c>orkeon email</c> (STUDIO-69): <c>accounts --json</c> to say whether each
/// account is ready, <c>check</c> to prove it connects. Studio hosts neither the mail libraries
/// nor the token store — the CLI is the authority, and it reads the settings file as saved.
/// <para>
/// Modelled on <c>UseCaseClient</c>: failures are typed rather than thrown — a missing binary, a
/// refusal, an answer that is no JSON and a cancellation each come back as a result. Standard
/// output carries the answer alone; the verb's "Using settings" line and its log lines go to
/// standard error and are only read to explain a failure.
/// </para>
/// </summary>
public sealed class EmailCliClient
{
    private const string SettingsLinePrefix = "Using settings:";

    private readonly OrkeonProcessRunner _runner;

    /// <summary>
    /// Creates a client over <paramref name="runner"/> — in the window, the runner the doctor and
    /// the launcher share, so an account is never judged by another binary than the one that runs
    /// the teams.
    /// </summary>
    public EmailCliClient(OrkeonProcessRunner runner) =>
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    /// <summary>
    /// Runs <c>email accounts --json</c> on <paramref name="settingsPath"/> and reads the list.
    /// No network is involved. Never throws for what the process did.
    /// </summary>
    public async Task<EmailAccountsResult> ListAsync(string settingsPath, CancellationToken cancellationToken = default)
    {
        var (run, stdout, stderr) = await RunAsync(EmailArgumentsBuilder.BuildAccounts(settingsPath), cancellationToken).ConfigureAwait(false);

        if (FailureOf(run, stderr, EmailArgumentsBuilder.AccountsVerb) is { } failure)
            return new EmailAccountsResult { Failure = failure };

        return EmailAccountsParser.TryParse(string.Join('\n', stdout), out var accounts, out var error)
            ? new EmailAccountsResult { Accounts = accounts }
            : new EmailAccountsResult { Failure = new EmailCliFailure(EmailCliFailureKind.Unreadable, error ?? "", run.ExitCode) };
    }

    /// <summary>
    /// Runs <c>email check</c> on <paramref name="account"/>: a real connection to the provider.
    /// Cancelling <paramref name="cancellationToken"/> stops the child. Never throws for what the
    /// process did.
    /// </summary>
    public async Task<EmailCheckOutcome> CheckAsync(string account, string settingsPath, CancellationToken cancellationToken = default)
    {
        var (run, stdout, stderr) = await RunAsync(EmailArgumentsBuilder.BuildCheck(account, settingsPath), cancellationToken).ConfigureAwait(false);

        return run.Outcome switch
        {
            RunOutcome.NotStarted => new EmailCheckOutcome(EmailCheckKind.Unavailable, run.Description),
            RunOutcome.Cancelled => new EmailCheckOutcome(EmailCheckKind.Cancelled, run.Description, run.ExitCode),
            RunOutcome.Success => new EmailCheckOutcome(
                EmailCheckKind.Reachable, stdout.LastOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? run.Description, run.ExitCode),
            RunOutcome.ScriptError => new EmailCheckOutcome(EmailCheckKind.OperatorFixable, Said(run, stderr, EmailArgumentsBuilder.CheckVerb), run.ExitCode),
            RunOutcome.RuntimeError => new EmailCheckOutcome(EmailCheckKind.ServerOrNetwork, Said(run, stderr, EmailArgumentsBuilder.CheckVerb), run.ExitCode),
            _ => new EmailCheckOutcome(EmailCheckKind.Unavailable, run.Description, run.ExitCode),
        };
    }

    private async Task<(ProcessRunResult Run, List<string> Stdout, List<string> Stderr)> RunAsync(
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

    /// <summary>Why the run carries no list to read; null when it ended on exit code 0.</summary>
    private static EmailCliFailure? FailureOf(ProcessRunResult run, List<string> stderr, string verb) => run.Outcome switch
    {
        RunOutcome.Success => null,
        RunOutcome.NotStarted => new EmailCliFailure(EmailCliFailureKind.EngineMissing, run.Description),
        RunOutcome.Cancelled => new EmailCliFailure(EmailCliFailureKind.Cancelled, run.Description, run.ExitCode),
        _ => new EmailCliFailure(EmailCliFailureKind.Stopped, Said(run, stderr, verb), run.ExitCode),
    };

    /// <summary>
    /// What a verb that failed said: its own <c>orkeon email &lt;verb&gt;: …</c> line without that
    /// prefix, else the last thing on standard error that is not the settings line, else what the
    /// exit code means.
    /// </summary>
    private static string Said(ProcessRunResult run, List<string> stderr, string verb)
    {
        var prefix = $"orkeon {EmailArgumentsBuilder.Verb} {verb}: ";
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

/// <summary>
/// Reads the <c>--json</c> payload of <c>orkeon email accounts</c> — one array of objects, one
/// per account. Tolerant on the model of <c>DoctorReportParser</c>: the array is located inside
/// the output, a property Studio does not know is ignored, a property the CLI did not write takes
/// its default, and an entry without a name is skipped. A version of the CLI that adds a field
/// stays readable; one that prints no array at all reports why rather than an empty list.
/// </summary>
public static class EmailAccountsParser
{
    /// <summary>Parses the accounts out of <paramref name="output"/>.</summary>
    /// <returns>False when no JSON array could be read; <paramref name="error"/> then says why.</returns>
    public static bool TryParse(string? output, out IReadOnlyList<EmailAccountState> accounts, out string? error)
    {
        accounts = [];

        if (string.IsNullOrWhiteSpace(output))
        {
            error = "orkeon email accounts produced no output — a JSON array was expected.";
            return false;
        }

        var start = output.IndexOf('[', StringComparison.Ordinal);
        var end = output.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            error = "orkeon email accounts did not print a JSON array — was it run without --json?";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(output[start..(end + 1)]);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "orkeon email accounts printed JSON that is not an array of accounts.";
                return false;
            }

            var parsed = new List<EmailAccountState>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (TryReadAccount(element, out var account))
                    parsed.Add(account);
            }

            accounts = parsed;
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"orkeon email accounts printed malformed JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadAccount(JsonElement element, out EmailAccountState account)
    {
        account = default!;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        var name = ReadString(element, "name");
        if (string.IsNullOrWhiteSpace(name))
            return false;

        account = new EmailAccountState
        {
            Name = name,
            Ready = ReadBoolean(element, "ready"),
            Problem = ReadString(element, "problem"),
            Provider = ReadString(element, "provider"),
            Reads = ReadString(element, "reads"),
            Sends = ReadString(element, "sends"),
            Auth = ReadString(element, "auth"),
            IsDefault = ReadBoolean(element, "default"),
        };
        return true;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
}
