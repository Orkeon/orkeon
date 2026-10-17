using System.Text.Json;
using Orkeon.Studio.Core.Email;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Drive;

/// <summary>
/// The <c>orkeon drive</c> argv Studio sends, on the model of <see cref="EmailArgumentsBuilder"/>.
/// Every verb always names the settings file: without <c>--settings</c> the verb resolves its
/// chain from the working directory of Studio and may read another file than the one the screen
/// shows — and a sign-in would store its tokens beside that other file. The drift pin is
/// <c>DriveCliClientTests</c>' golden argv.
/// </summary>
public static class DriveArgumentsBuilder
{
    /// <summary>The CLI verb.</summary>
    public const string Verb = "drive";

    /// <summary>The sub-verb that lists the accounts.</summary>
    public const string AccountsVerb = "accounts";

    /// <summary>The sub-verb that connects to one account.</summary>
    public const string CheckVerb = "check";

    /// <summary>The sub-verb that signs an account in.</summary>
    public const string LoginVerb = "login";

    /// <summary>The sub-verb that forgets the tokens of an account.</summary>
    public const string LogoutVerb = "logout";

    /// <summary><c>drive accounts --json --settings &lt;file&gt;</c>: every account and whether it is ready, no network.</summary>
    public static IReadOnlyList<string> BuildAccounts(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        return [Verb, AccountsVerb, "--json", "--settings", settingsPath];
    }

    /// <summary>
    /// <c>drive check &lt;account&gt; --events jsonl --settings &lt;file&gt;</c>: a real connection that
    /// finds the root folder and lists it, its verdict one event line.
    /// </summary>
    public static IReadOnlyList<string> BuildCheck(string account, string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        return [Verb, CheckVerb, account, "--events", "jsonl", "--settings", settingsPath];
    }

    /// <summary>
    /// <c>drive login &lt;account&gt; --events jsonl --settings &lt;file&gt;</c>: the interactive sign-in,
    /// each step an event line, the pasted redirect address read on standard input.
    /// </summary>
    public static IReadOnlyList<string> BuildLogin(string account, string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        return [Verb, LoginVerb, account, "--events", "jsonl", "--settings", settingsPath];
    }

    /// <summary><c>drive logout &lt;account&gt; --settings &lt;file&gt;</c>: the stored tokens are forgotten.</summary>
    public static IReadOnlyList<string> BuildLogout(string account, string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        return [Verb, LogoutVerb, account, "--settings", settingsPath];
    }
}

/// <summary>
/// One account as <c>orkeon drive accounts --json</c> describes it (<c>DriveAccountStatus</c>).
/// Nothing here is computed by Studio: whether an account is ready depends on the environment and
/// the token store of the <c>orkeon</c> process.
/// </summary>
public sealed record DriveAccountState
{
    /// <summary>What the verb writes as the provider of an account the engine set aside.</summary>
    public const string SetAsideProvider = "?";

    /// <summary>The account name, as the engine lists it.</summary>
    public required string Name { get; init; }

    /// <summary>Whether it has everything it needs to connect — checked by the engine without network.</summary>
    public bool Ready { get; init; }

    /// <summary>The engine's sentence on what to fix, as printed, when it is not ready.</summary>
    public string? Problem { get; init; }

    /// <summary>The drive (<c>GoogleDrive</c>, <c>OneDrive</c>), or <see cref="SetAsideProvider"/>.</summary>
    public string? Provider { get; init; }

    /// <summary>The rights it grants, as the engine spells them.</summary>
    public string? Rights { get; init; }

    /// <summary>The folder it is confined to, <c>/</c> for the whole drive.</summary>
    public string? Root { get; init; }

    /// <summary>Whether it is the account a call without <c>account</c> uses.</summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// Whether the engine set the account aside: its declaration is refused, so no run will use
    /// it. Read off the marker the verb writes, never out of <see cref="Problem"/>.
    /// </summary>
    public bool IsSetAside => string.Equals(Provider, SetAsideProvider, StringComparison.Ordinal);
}

/// <summary>The drive accounts of a settings file, or why they could not be listed.</summary>
public sealed record DriveAccountsResult
{
    /// <summary>The accounts, in the engine's order.</summary>
    public IReadOnlyList<DriveAccountState> Accounts { get; init; } = [];

    /// <summary>Why no list came back; null on success.</summary>
    public EmailCliFailure? Failure { get; init; }

    /// <summary>The account named <paramref name="name"/>, compared without regard to case as the engine does; null when the list has none.</summary>
    public DriveAccountState? Find(string name) =>
        Accounts.FirstOrDefault(account => string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The Studio side of <c>orkeon drive</c>: <c>accounts --json</c> to say whether each account is
/// ready, <c>check</c> to prove it connects, <c>login --events jsonl</c> and <c>logout</c> to sign
/// it in and out. Studio hosts neither the drive clients, nor an OAuth flow, nor the token store —
/// the CLI is the authority, and it reads the settings file as saved.
/// <para>
/// The sign-in and the check speak the protocol of <c>orkeon email</c>, event for event under the
/// <c>drive.</c> prefix (<c>DriveEventKinds</c>): their steps, verdicts and failures are the
/// e-mail family's types (<see cref="EmailLoginStep"/>, <see cref="EmailCheckOutcome"/>,
/// <see cref="EmailCliFailure"/>), read by the same code.
/// </para>
/// </summary>
public sealed class DriveCliClient
{
    private readonly AccountCli _cli;

    /// <summary>
    /// Creates a client over <paramref name="runner"/> — in the window, the runner the doctor and
    /// the launcher share, so an account is never judged by another binary than the one that runs
    /// the teams.
    /// </summary>
    public DriveCliClient(OrkeonProcessRunner runner) =>
        _cli = new AccountCli(runner ?? throw new ArgumentNullException(nameof(runner)), AccountCliProtocol.Drive);

    /// <summary>
    /// Runs <c>drive accounts --json</c> on <paramref name="settingsPath"/> and reads the list.
    /// No network is involved. Never throws for what the process did.
    /// </summary>
    public async Task<DriveAccountsResult> ListAsync(string settingsPath, CancellationToken cancellationToken = default)
    {
        var (run, stdout, stderr) = await _cli.RunAsync(DriveArgumentsBuilder.BuildAccounts(settingsPath), cancellationToken).ConfigureAwait(false);

        if (_cli.FailureOf(run, stderr, DriveArgumentsBuilder.AccountsVerb) is { } failure)
            return new DriveAccountsResult { Failure = failure };

        return DriveAccountsParser.TryParse(string.Join('\n', stdout), out var accounts, out var error)
            ? new DriveAccountsResult { Accounts = accounts }
            : new DriveAccountsResult { Failure = new EmailCliFailure(EmailCliFailureKind.Unreadable, error ?? "", run.ExitCode) };
    }

    /// <summary>
    /// Runs <c>drive check</c> on <paramref name="account"/>: a real connection to the drive.
    /// Cancelling <paramref name="cancellationToken"/> stops the child. Never throws for what the
    /// process did.
    /// </summary>
    public Task<EmailCheckOutcome> CheckAsync(string account, string settingsPath, CancellationToken cancellationToken = default) =>
        _cli.CheckAsync(DriveArgumentsBuilder.BuildCheck(account, settingsPath), DriveArgumentsBuilder.CheckVerb, cancellationToken);

    /// <summary>
    /// Runs <c>drive login --events jsonl</c> on <paramref name="account"/> and follows it, as
    /// <see cref="EmailCliClient.LoginAsync"/> follows <c>email login</c>: <paramref name="onStep"/>
    /// receives the device code (OneDrive), the address to open (Google Drive) and each pasted
    /// address the verb rejected; the task ends on <see cref="EmailLoginStepKind.Completed"/> or
    /// <see cref="EmailLoginStepKind.Failed"/>. Never throws for what the process did.
    /// </summary>
    public Task<EmailLoginStep> LoginAsync(
        string account,
        string settingsPath,
        Action<EmailLoginStep> onStep,
        EmailLoginInput? input = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onStep);
        var arguments = DriveArgumentsBuilder.BuildLogin(account, settingsPath);
        return _cli.LoginAsync(arguments, DriveArgumentsBuilder.LoginVerb, onStep, input, cancellationToken);
    }

    /// <summary>
    /// Runs <c>drive logout</c> on <paramref name="account"/>: the stored tokens are forgotten.
    /// Null when the verb ended well, why it did not otherwise. Never throws for what the process did.
    /// </summary>
    public Task<EmailCliFailure?> LogoutAsync(string account, string settingsPath, CancellationToken cancellationToken = default) =>
        _cli.LogoutAsync(DriveArgumentsBuilder.BuildLogout(account, settingsPath), DriveArgumentsBuilder.LogoutVerb, cancellationToken);
}

/// <summary>
/// Reads the <c>--json</c> payload of <c>orkeon drive accounts</c> — one array of objects, one per
/// account — as tolerantly as <see cref="EmailAccountsParser"/>: the array is located inside the
/// output, a property Studio does not know is ignored, one the CLI did not write takes its default,
/// and an entry without a name is skipped.
/// </summary>
public static class DriveAccountsParser
{
    /// <summary>Parses the accounts out of <paramref name="output"/>.</summary>
    /// <returns>False when no JSON array could be read; <paramref name="error"/> then says why.</returns>
    public static bool TryParse(string? output, out IReadOnlyList<DriveAccountState> accounts, out string? error)
    {
        accounts = [];

        if (string.IsNullOrWhiteSpace(output))
        {
            error = "orkeon drive accounts produced no output — a JSON array was expected.";
            return false;
        }

        var start = output.IndexOf('[', StringComparison.Ordinal);
        var end = output.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            error = "orkeon drive accounts did not print a JSON array — was it run without --json?";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(output[start..(end + 1)]);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "orkeon drive accounts printed JSON that is not an array of accounts.";
                return false;
            }

            var parsed = new List<DriveAccountState>();
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
            error = $"orkeon drive accounts printed malformed JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadAccount(JsonElement element, out DriveAccountState account)
    {
        account = default!;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        var name = ReadString(element, "name");
        if (string.IsNullOrWhiteSpace(name))
            return false;

        account = new DriveAccountState
        {
            Name = name,
            Ready = ReadBoolean(element, "ready"),
            Problem = ReadString(element, "problem"),
            Provider = ReadString(element, "provider"),
            Rights = ReadString(element, "rights"),
            Root = ReadString(element, "root"),
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
