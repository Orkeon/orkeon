using MimeKit;
using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>One message a <see cref="FakeMailSender"/> was asked to send.</summary>
/// <param name="Sender">Envelope sender address.</param>
/// <param name="Recipients">Envelope recipient addresses, in order.</param>
/// <param name="Mime">The message as it would have travelled.</param>
internal sealed record SentMail(string Sender, IReadOnlyList<string> Recipients, string Mime);

/// <summary>
/// <see cref="IMailSender"/> that records each message with its explicit envelope, or throws
/// <see cref="Failure"/> when set.
/// </summary>
internal sealed class FakeMailSender : IMailSender
{
    /// <summary>Messages sent, in order.</summary>
    public List<SentMail> Sent { get; } = [];

    /// <summary>When set, every send throws it.</summary>
    public Exception? Failure { get; set; }

    /// <inheritdoc />
    public async Task<SendReceipt> SendAsync(
        MimeMessage message, MailboxAddress sender, IReadOnlyList<MailboxAddress> recipients, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
            throw failure;

        Sent.Add(new SentMail(sender.Address, recipients.Select(recipient => recipient.Address).ToList(),
            await FakeMailbox.ToTextAsync(message, cancellationToken)));
        return new SendReceipt("250 2.0.0 OK");
    }
}
