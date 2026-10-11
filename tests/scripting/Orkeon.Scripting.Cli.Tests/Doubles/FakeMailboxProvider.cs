using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Scripting.Cli.Tests.Doubles;

/// <summary>A real mailbox that must never be reached: records every request and throws.</summary>
internal sealed class FakeMailboxProvider : IMailboxProvider
{
    /// <summary>One entry per GetMailbox / GetSender call.</summary>
    public List<string> Requests { get; } = [];

    /// <inheritdoc />
    public IMailbox GetMailbox(ResolvedEmailAccount account)
    {
        Requests.Add("mailbox:" + account.Name);
        throw new InvalidOperationException("a real mailbox was reached");
    }

    /// <inheritdoc />
    public IMailSender GetSender(ResolvedEmailAccount account)
    {
        Requests.Add("sender:" + account.Name);
        throw new InvalidOperationException("a real mailbox was reached");
    }
}
