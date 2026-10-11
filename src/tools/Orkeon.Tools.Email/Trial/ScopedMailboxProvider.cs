using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Tools.Email.Trial;

internal sealed class ScopedMailboxProvider(IMailboxProvider real) : IMailboxProvider
{
    public IMailbox GetMailbox(ResolvedEmailAccount account) =>
        EmailTrial.Current is { } trial ? trial.MailboxFor(account.Name) : real.GetMailbox(account);

    public IMailSender GetSender(ResolvedEmailAccount account) =>
        EmailTrial.Current is { } trial ? trial.MailboxFor(account.Name) : real.GetSender(account);
}
