using System.Globalization;
using MimeKit;

namespace Orkeon.Tools.Email.Mime;

/// <summary>One attachment of a message, in the order every tool numbers them.</summary>
/// <param name="Index">Zero-based position, stable for a given message.</param>
/// <param name="FileName">Sanitized file name, safe to write.</param>
/// <param name="ContentType">MIME type.</param>
/// <param name="SizeBytes">Decoded size, estimated from the encoded size.</param>
/// <param name="Inline">Whether the part is displayed inline (an embedded image) rather than attached.</param>
/// <param name="Entity">The MIME entity, to decode it.</param>
internal sealed record AttachmentEntry(int Index, string FileName, string ContentType, long SizeBytes, bool Inline, MimeEntity Entity);

/// <summary>The body of a message as an agent reads it.</summary>
/// <param name="Text">Plain text: the text part, else the HTML part rendered.</param>
/// <param name="HadHiddenContent">Whether the HTML part hid text from a human reader.</param>
internal sealed record MessageBody(string Text, bool HadHiddenContent);

/// <summary>
/// Reads what the tools expose out of a <see cref="MimeMessage"/>: addresses, body text and the
/// attachment list. Mailbox messages and <c>.eml</c> files go through the same code.
/// </summary>
internal static class MimeMessageReader
{
    /// <summary>The body text of <paramref name="message"/>.</summary>
    public static MessageBody ReadBody(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var plain = message.TextBody;
        var html = message.HtmlBody;

        if (html is not null)
        {
            var rendered = HtmlTextRenderer.Render(html);
            return new MessageBody(plain is not null ? HtmlTextRenderer.Normalize(plain) : rendered.Text, rendered.HadHiddenContent);
        }

        return new MessageBody(plain is null ? string.Empty : HtmlTextRenderer.Normalize(plain), false);
    }

    /// <summary>The attachments of <paramref name="message"/>, numbered from zero.</summary>
    public static IReadOnlyList<AttachmentEntry> ReadAttachments(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var entries = new List<AttachmentEntry>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in message.BodyParts)
        {
            switch (entity)
            {
                case MessagePart forwarded:
                {
                    var declared = forwarded.ContentDisposition?.FileName
                        ?? forwarded.ContentType.Name
                        ?? (forwarded.Message?.Subject is { Length: > 0 } subject ? subject + ".eml" : null);
                    var name = AttachmentNames.Unique(AttachmentNames.Sanitize(declared, entries.Count, forwarded.ContentType), taken);
                    entries.Add(new AttachmentEntry(entries.Count, name, "message/rfc822", MeasureMessage(forwarded), false, forwarded));
                    break;
                }

                case MimePart part when IsAttachment(part):
                {
                    var name = AttachmentNames.Unique(AttachmentNames.Sanitize(part.FileName, entries.Count, part.ContentType), taken);
                    var inline = !part.IsAttachment;
                    entries.Add(new AttachmentEntry(entries.Count, name, part.ContentType.MimeType, EstimateDecodedSize(part), inline, part));
                    break;
                }
            }
        }

        return entries;
    }

    /// <summary>
    /// A leaf part is an attachment when it says so, or when it carries a file name and is not
    /// one of the message's own text bodies (an inline image, a calendar invite).
    /// </summary>
    private static bool IsAttachment(MimePart part)
    {
        if (part.IsAttachment)
            return true;
        if (string.IsNullOrEmpty(part.FileName))
            return false;
        return !(part.ContentType.IsMimeType("text", "plain") || part.ContentType.IsMimeType("text", "html"));
    }

    /// <summary>Decodes <paramref name="entry"/> into <paramref name="destination"/>.</summary>
    public static async Task WriteAttachmentAsync(AttachmentEntry entry, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(destination);
        switch (entry.Entity)
        {
            case MessagePart forwarded when forwarded.Message is not null:
                await forwarded.Message.WriteToAsync(destination, cancellationToken).ConfigureAwait(false);
                break;
            case MimePart { Content: not null } part:
                await part.Content.DecodeToAsync(destination, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    /// <summary><c>Name &lt;address&gt;</c>, or the bare address.</summary>
    public static string Format(InternetAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address is MailboxAddress mailbox)
        {
            return string.IsNullOrWhiteSpace(mailbox.Name)
                ? mailbox.Address
                : string.Create(CultureInfo.InvariantCulture, $"{mailbox.Name} <{mailbox.Address}>");
        }

        return address.ToString();
    }

    /// <summary>Every address of <paramref name="list"/>, formatted.</summary>
    public static IReadOnlyList<string> Format(InternetAddressList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return list.Mailboxes.Select(Format).ToList();
    }

    /// <summary>The message date in ISO 8601, or null when the header is absent.</summary>
    public static string? FormatDate(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message.Date == DateTimeOffset.MinValue ? null : message.Date.ToString("O", CultureInfo.InvariantCulture);
    }

    private static long EstimateDecodedSize(MimePart part)
    {
        var stream = part.Content?.Stream;
        if (stream is null || !stream.CanSeek)
            return 0;

        var encoded = stream.Length;
        return part.ContentTransferEncoding == ContentEncoding.Base64 ? encoded * 3 / 4 : encoded;
    }

    private static long MeasureMessage(MessagePart part) =>
        part.Message is null ? 0 : Mailboxes.MessageSizes.Measure(part.Message);
}
