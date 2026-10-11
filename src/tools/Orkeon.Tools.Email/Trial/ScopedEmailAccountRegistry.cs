using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Tools.Email.Trial;

/// <summary>
/// The account registry as a trial sees it: with no account configured, a trial offers its own
/// account, <c>trial</c>, as the default; otherwise, and outside a trial, the real registry answers.
/// </summary>
internal sealed class ScopedEmailAccountRegistry(IEmailAccountRegistry real) : IEmailAccountRegistry
{
    private static readonly string[] TrialNames = [EmailTrial.Account.Name];

    public IReadOnlyList<string> Names => Offers ? TrialNames : real.Names;

    public string? DefaultName => Offers ? EmailTrial.Account.Name : real.DefaultName;

    public ResolvedEmailAccount Resolve(string? name) =>
        Offers && IsTrialAccount(name) ? EmailTrial.Account : real.Resolve(name);

    public EmailAccountResolution Inspect(string name) =>
        Offers && IsTrialAccount(name) ? new EmailAccountResolution(EmailTrial.Account, []) : real.Inspect(name);

    private bool Offers => EmailTrial.Current is not null && real.Names.Count == 0;

    private static bool IsTrialAccount(string? name) =>
        string.IsNullOrWhiteSpace(name) || string.Equals(name.Trim(), EmailTrial.Account.Name, StringComparison.OrdinalIgnoreCase);
}
