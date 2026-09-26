using System.Globalization;
using System.Net;
using System.Text;
using MimeKit;
using MimeKit.Utils;
using Orkeon.Domain.FileSystem;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Tools.Email.Mime;

/// <summary>What an agent asked to write. Exactly one of <see cref="ReplyTo"/> and <see cref="Forward"/> may be set.</summary>
internal sealed record ComposeInput
{
    /// <summary>Primary recipients, <c>address</c> or <c>Name &lt;address&gt;</c>.</summary>
    public IReadOnlyList<string> To { get; init; } = [];

    /// <summary>Copy recipients.</summary>
    public IReadOnlyList<string> Cc { get; init; } = [];

    /// <summary>Blind-copy recipients.</summary>
    public IReadOnlyList<string> Bcc { get; init; } = [];

    /// <summary>Subject; derived from the original for a reply or a forward when absent.</summary>
    public string? Subject { get; init; }

    /// <summary>Plain-text body.</summary>
    public string? Text { get; init; }

    /// <summary>Optional HTML body; a text alternative is derived when <see cref="Text"/> is absent.</summary>
    public string? Html { get; init; }

    /// <summary>Virtual paths of files to attach, read through the VFS.</summary>
    public IReadOnlyList<string> Attachments { get; init; } = [];

    /// <summary>The message being answered.</summary>
    public MimeMessage? ReplyTo { get; init; }

    /// <summary>Whether the reply goes to every original recipient, not only the sender.</summary>
    public bool ReplyAll { get; init; }

    /// <summary>Whether the reply quotes the original text.</summary>
    public bool QuoteOriginal { get; init; } = true;

    /// <summary>The message being forwarded, attached whole.</summary>
    public MimeMessage? Forward { get; init; }
}

/// <summary>
/// Builds the MIME message an account sends or saves as a draft. <c>From</c> is always the
/// account's own address: an agent chooses what to say and to whom, never who is speaking.
/// </summary>
internal sealed class MessageComposer
{
    private const int MaxQuotedCharacters = 20_000;

    private readonly IFileSystemService _fileSystem;

    /// <summary>Creates the composer; attachments are read through <paramref name="fileSystem"/>.</summary>
    public MessageComposer(IFileSystemService fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <summary>Builds the message. The caller owns (and disposes) the result.</summary>
    public async Task<MimeMessage> ComposeAsync(ResolvedEmailAccount account, ComposeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(input);
        if (input.ReplyTo is not null && input.Forward is not null)
            throw new EmailToolException(EmailErrorCode.InvalidRequest, "A message is either a reply or a forward, not both: pass reply_to_id or forward_id.");

        var message = new MimeMessage();
        try
        {
            message.From.Add(new MailboxAddress(account.DisplayName, account.Address));
            AddRecipients(message.To, input.To);
            AddRecipients(message.Cc, input.Cc);
            AddRecipients(message.Bcc, input.Bcc);

            if (input.ReplyTo is { } original)
                PrepareReply(message, original, input, account.Address);
            else if (input.Forward is { } forwarded)
                message.Subject = WithPrefix("Fwd: ", input.Subject, forwarded.Subject, "Fwd:", "Fw:");
            else
                message.Subject = RequireSubject(input.Subject);

            if (message.To.Count + message.Cc.Count + message.Bcc.Count == 0)
                throw new EmailToolException(EmailErrorCode.InvalidRequest, "The message has no recipient: pass `to`.");

            var body = new BodyBuilder();
            ComposeText(body, input);
            await AttachFilesAsync(body, input.Attachments, cancellationToken).ConfigureAwait(false);
            if (input.Forward is { } attachedOriginal)
                body.Attachments.Add(ForwardedPart(attachedOriginal));

            message.Body = body.ToMessageBody();
            message.Date = DateTimeOffset.Now;
            message.MessageId = MimeUtils.GenerateMessageId(DomainOf(account.Address));
            return message;
        }
        catch
        {
            message.Dispose();
            throw;
        }
    }

    private static void AddRecipients(InternetAddressList list, IReadOnlyList<string> values)
    {
        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            if (!MailboxAddress.TryParse(value.Trim(), out var mailbox) || !mailbox.Address.Contains('@', StringComparison.Ordinal))
                throw new EmailToolException(EmailErrorCode.InvalidRequest, $"'{value}' is not an e-mail address.");
            if (!list.Mailboxes.Any(existing => string.Equals(existing.Address, mailbox.Address, StringComparison.OrdinalIgnoreCase)))
                list.Add(mailbox);
        }
    }

    private static void PrepareReply(MimeMessage message, MimeMessage original, ComposeInput input, string ownAddress)
    {
        message.Subject = WithPrefix("Re: ", input.Subject, original.Subject, "Re:");
        if (!string.IsNullOrEmpty(original.MessageId))
        {
            message.InReplyTo = original.MessageId;
            foreach (var reference in original.References)
                message.References.Add(reference);
            message.References.Add(original.MessageId);
        }

        if (message.To.Count == 0)
        {
            var answerTo = original.ReplyTo.Mailboxes.Any() ? original.ReplyTo.Mailboxes : original.From.Mailboxes;
            AddUnique(message.To, answerTo, ownAddress);
        }

        if (input.ReplyAll)
        {
            AddUnique(message.To, original.To.Mailboxes, ownAddress);
            AddUnique(message.Cc, original.Cc.Mailboxes, ownAddress);
        }
    }

    private static void AddUnique(InternetAddressList list, IEnumerable<MailboxAddress> candidates, string ownAddress)
    {
        foreach (var candidate in candidates)
        {
            if (string.Equals(candidate.Address, ownAddress, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!list.Mailboxes.Any(existing => string.Equals(existing.Address, candidate.Address, StringComparison.OrdinalIgnoreCase)))
                list.Add(new MailboxAddress(candidate.Name, candidate.Address));
        }
    }

    private static string RequireSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            throw new EmailToolException(EmailErrorCode.InvalidRequest, "A new message needs a `subject`.");
        return subject.Trim();
    }

    private static string WithPrefix(string prefix, string? explicitSubject, string? originalSubject, params string[] existingPrefixes)
    {
        if (!string.IsNullOrWhiteSpace(explicitSubject))
            return explicitSubject.Trim();

        var subject = originalSubject?.Trim() ?? string.Empty;
        return existingPrefixes.Any(p => subject.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ? subject : prefix + subject;
    }

    private static void ComposeText(BodyBuilder body, ComposeInput input)
    {
        var text = input.Text;
        if (text is null && input.Html is not null)
            text = HtmlTextRenderer.Render(input.Html).Text;

        if (text is null && input.ReplyTo is null && input.Forward is null)
            throw new EmailToolException(EmailErrorCode.InvalidRequest, "The message has no body: pass `text` (or `html`).");

        var quote = input.ReplyTo is { } original && input.QuoteOriginal ? Quote(original) : null;
        body.TextBody = quote is null ? text ?? string.Empty : $"{text}\n\n{quote}";

        if (input.Html is not null)
        {
            body.HtmlBody = quote is null
                ? input.Html
                : $"{input.Html}<br><blockquote>{WebUtility.HtmlEncode(quote).Replace("\n", "<br>", StringComparison.Ordinal)}</blockquote>";
        }
    }

    private static string Quote(MimeMessage original)
    {
        var text = MimeMessageReader.ReadBody(original).Text;
        if (text.Length > MaxQuotedCharacters)
            text = string.Concat(text.AsSpan(0, MaxQuotedCharacters), "\n[…]");

        var sender = original.From.Mailboxes.Select(MimeMessageReader.Format).FirstOrDefault() ?? "the sender";
        var header = original.Date == DateTimeOffset.MinValue
            ? $"{sender} wrote:"
            : string.Create(CultureInfo.InvariantCulture, $"On {original.Date:ddd, d MMM yyyy HH:mm zzz}, {sender} wrote:");

        var quoted = new StringBuilder(text.Length + 64).Append(header);
        foreach (var line in text.Split('\n'))
            quoted.Append('\n').Append("> ").Append(line);
        return quoted.ToString();
    }

    private async Task AttachFilesAsync(BodyBuilder body, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var check = _fileSystem.ResolveAndValidate(path, FileAccessRights.Read);
            if (!check.IsAllowed)
                throw new EmailToolException(EmailErrorCode.InvalidRequest, $"Cannot attach '{path}': {(check.DenialReason ?? "the path is not readable").TrimEnd('.')}.");

            var fileName = AttachmentNames.Sanitize(path, body.Attachments.Count, null);
            var stream = await _fileSystem.OpenReadStreamAsync(path, cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                // The Stream overload copies the content; the overloads taking only a file NAME
                // open the disk directly and must never be used here (VFS compliance).
                await body.Attachments.AddAsync(fileName, stream, ContentType.Parse(MimeTypes.GetMimeType(fileName)), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static MessagePart ForwardedPart(MimeMessage original)
    {
        var name = AttachmentNames.Sanitize(
            string.IsNullOrWhiteSpace(original.Subject) ? "forwarded.eml" : original.Subject + ".eml", 0, null);
        return new MessagePart
        {
            Message = original,
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = name },
        };
    }

    private static string DomainOf(string address)
    {
        var at = address.LastIndexOf('@');
        return at >= 0 && at < address.Length - 1 ? address[(at + 1)..] : "localhost";
    }
}
