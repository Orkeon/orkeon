using MimeKit;
using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Dtos;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Mime;

namespace Orkeon.Tools.Email.Tools;

/// <summary>Builds the MIME message of <c>email_send</c> and <c>email_draft</c>, originals included.</summary>
internal static class EmailComposeHelpers
{
    /// <summary>The rights composing needs beyond <paramref name="baseRight"/>: answering or forwarding reads the original.</summary>
    public static EmailRights RightsFor(EmailComposeRequest request, EmailRights baseRight)
    {
        ArgumentNullException.ThrowIfNull(request);
        return string.IsNullOrWhiteSpace(request.ReplyToId) && string.IsNullOrWhiteSpace(request.ForwardId)
            ? baseRight
            : baseRight | EmailRights.Read;
    }

    /// <summary>Composes the message; the caller disposes it.</summary>
    public static async Task<MimeMessage> ComposeAsync(
        EmailComposeRequest request, ResolvedEmailAccount account, IMailbox mailbox, IFileSystemService fileSystem, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        FetchedMessage? replyTo = null;
        FetchedMessage? forward = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(request.ReplyToId))
                replyTo = await mailbox.GetMessageAsync(request.ReplyToId.Trim(), cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(request.ForwardId))
                forward = await mailbox.GetMessageAsync(request.ForwardId.Trim(), cancellationToken).ConfigureAwait(false);

            var input = new ComposeInput
            {
                To = request.To ?? [],
                Cc = request.Cc ?? [],
                Bcc = request.Bcc ?? [],
                Subject = request.Subject,
                Text = request.Text,
                Html = request.Html,
                Attachments = request.Attachments ?? [],
                ReplyTo = replyTo?.Message,
                ReplyAll = request.ReplyAll ?? false,
                QuoteOriginal = request.QuoteOriginal ?? true,
                Forward = forward?.Message,
            };

            return await new MessageComposer(fileSystem).ComposeAsync(account, input, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Composed, the forwarded original belongs to the new message; refused, it is ours.
            forward?.Dispose();
            throw;
        }
        finally
        {
            // A forwarded original now lives inside the composed message, which disposes it.
            replyTo?.Dispose();
        }
    }

    /// <summary>Every recipient of <paramref name="message"/> (To, Cc and Bcc), each address once.</summary>
    public static IReadOnlyList<MailboxAddress> Recipients(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.To.Mailboxes.Concat(message.Cc.Mailboxes).Concat(message.Bcc.Mailboxes)
            .DistinctBy(mailbox => mailbox.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
