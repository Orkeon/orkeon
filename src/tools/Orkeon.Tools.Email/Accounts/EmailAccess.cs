using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Tools.Email.Accounts;

/// <summary>
/// The gate every tool goes through: it resolves the account, checks that the account grants
/// the right the operation needs and that its backend can do it, and only then hands out the
/// mailbox. Refusals say which right or capability is missing and where an operator adds it.
/// </summary>
internal sealed class EmailAccess
{
    private readonly IEmailAccountRegistry _accounts;
    private readonly IMailboxProvider _mailboxes;

    /// <summary>Creates the gate.</summary>
    public EmailAccess(IEmailAccountRegistry accounts, IMailboxProvider mailboxes)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(mailboxes);
        _accounts = accounts;
        _mailboxes = mailboxes;
    }

    /// <summary>The registry behind the gate.</summary>
    public IEmailAccountRegistry Accounts => _accounts;

    /// <summary>The account named <paramref name="account"/>, checked for <paramref name="right"/>.</summary>
    public ResolvedEmailAccount Authorize(string? account, EmailRights right)
    {
        var resolved = _accounts.Resolve(account);
        Demand(resolved, right);
        return resolved;
    }

    /// <summary>The mailbox of an authorized account, checked for <paramref name="capability"/>.</summary>
    public IMailbox Mailbox(ResolvedEmailAccount account, MailboxCapabilities capability = MailboxCapabilities.None)
    {
        ArgumentNullException.ThrowIfNull(account);
        var mailbox = _mailboxes.GetMailbox(account);
        if (capability != MailboxCapabilities.None && (mailbox.Capabilities & capability) != capability)
        {
            throw new EmailToolException(
                EmailErrorCode.Unsupported,
                $"E-mail account '{account.Name}' reads mail over {account.Incoming}, which has no {Describe(capability)}.");
        }

        return mailbox;
    }

    private static string Describe(MailboxCapabilities capability) => capability switch
    {
        MailboxCapabilities.Folders => "folders (switch the account to IMAP for that)",
        MailboxCapabilities.Move => "folders to move messages into (switch the account to IMAP for that)",
        MailboxCapabilities.Flags => "read or flagged marks (switch the account to IMAP for that)",
        MailboxCapabilities.Drafts => "drafts folder (switch the account to IMAP for that)",
        MailboxCapabilities.Trash => "trash: delete for good with `permanent: true` (needs the Purge right)",
        _ => capability.ToString(),
    };

    /// <summary>The sender of an authorized account.</summary>
    public IMailSender Sender(ResolvedEmailAccount account) => _mailboxes.GetSender(account);

    /// <summary>Throws unless <paramref name="account"/> grants <paramref name="right"/>.</summary>
    public static void Demand(ResolvedEmailAccount account, EmailRights right)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (right == EmailRights.None || account.Grants(right))
            return;

        throw new EmailToolException(
            EmailErrorCode.RightDenied,
            $"E-mail account '{account.Name}' does not grant the {right} right (it grants: {account.Rights}). " +
            $"An operator adds it under {Constants.EmailDefaults.SectionName}:Accounts:{account.Name}:Rights.");
    }
}
