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
    private const string SwitchToImap = "switch the account to IMAP (Incoming:Protocol Imap) for that";

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
                $"E-mail account '{account.Name}' reads mail over {ProtocolName(account.Incoming)}, which {Describe(capability)}.");
        }

        return mailbox;
    }

    private static string Describe(MailboxCapabilities capability) => capability switch
    {
        MailboxCapabilities.Folders => "has no folders: " + SwitchToImap,
        MailboxCapabilities.Move => "has no folders to move messages into: " + SwitchToImap,
        MailboxCapabilities.Flags => "has no read or flagged marks: " + SwitchToImap,
        MailboxCapabilities.Drafts => "has no drafts folder: " + SwitchToImap,
        MailboxCapabilities.Trash => "has no trash and deletes for good: pass `permanent: true` (needs the Purge right)",
        _ => $"has no {capability}",
    };

    private static string ProtocolName(IncomingProtocol protocol) => protocol switch
    {
        IncomingProtocol.Imap => "IMAP",
        IncomingProtocol.Pop3 => "POP3",
        _ => "Microsoft Graph",
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
