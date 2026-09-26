using MimeKit;

namespace Orkeon.Tools.Email.Mailboxes.Graph;

/// <summary>
/// Sends through Microsoft Graph <c>sendMail</c> in MIME form, which files the message in Sent
/// Items. Graph reads the envelope from the MIME headers, so the sender refuses a message whose
/// headers name anyone beyond the checked recipients.
/// </summary>
internal sealed class GraphMailSender : IMailSender
{
    private readonly GraphClient _graph;

    /// <summary>Creates the sender over <paramref name="graph"/>.</summary>
    public GraphMailSender(GraphClient graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _graph = graph;
    }

    /// <inheritdoc />
    public async Task<SendReceipt> SendAsync(
        MimeMessage message, MailboxAddress sender, IReadOnlyList<MailboxAddress> recipients, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(recipients);
        var headerRecipients = message.To.Mailboxes.Concat(message.Cc.Mailboxes).Concat(message.Bcc.Mailboxes)
            .Concat(message.ResentTo.Mailboxes).Concat(message.ResentCc.Mailboxes).Concat(message.ResentBcc.Mailboxes);
        if (headerRecipients.Any(address => !recipients.Any(allowed => string.Equals(allowed.Address, address.Address, StringComparison.OrdinalIgnoreCase))))
            throw new EmailToolException(EmailErrorCode.RecipientNotAllowed, "The message headers name a recipient outside the checked list; it was not sent.");

        using var _ = await _graph.PostMimeAsync(GraphClient.Resolve("me/sendMail"), message, cancellationToken).ConfigureAwait(false);
        return new SendReceipt("202 Accepted");
    }
}
