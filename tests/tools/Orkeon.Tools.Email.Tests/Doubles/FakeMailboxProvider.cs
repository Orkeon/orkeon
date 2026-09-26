using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>
/// <see cref="IMailboxProvider"/> handing out one <see cref="FakeMailbox"/> and one
/// <see cref="FakeMailSender"/> per account name, created on first use.
/// </summary>
internal sealed class FakeMailboxProvider : IMailboxProvider
{
    private readonly Dictionary<string, FakeMailbox> _mailboxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FakeMailSender> _senders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The account names a mailbox was requested for, in order.</summary>
    public List<string> MailboxRequests { get; } = [];

    /// <summary>The mailbox of <paramref name="account"/>.</summary>
    public FakeMailbox MailboxOf(string account)
    {
        if (!_mailboxes.TryGetValue(account, out var mailbox))
            _mailboxes[account] = mailbox = new FakeMailbox();
        return mailbox;
    }

    /// <summary>The sender of <paramref name="account"/>.</summary>
    public FakeMailSender SenderOf(string account)
    {
        if (!_senders.TryGetValue(account, out var sender))
            _senders[account] = sender = new FakeMailSender();
        return sender;
    }

    /// <inheritdoc />
    public IMailbox GetMailbox(ResolvedEmailAccount account)
    {
        MailboxRequests.Add(account.Name);
        return MailboxOf(account.Name);
    }

    /// <inheritdoc />
    public IMailSender GetSender(ResolvedEmailAccount account) => SenderOf(account.Name);
}
