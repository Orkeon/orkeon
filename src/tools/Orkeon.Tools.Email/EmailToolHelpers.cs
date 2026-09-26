using System.Globalization;
using MimeKit;
using Orkeon.Tools.Email.Constants;
using Orkeon.Tools.Email.Dtos;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Mime;
using Orkeon.Tools.Email.Security;

namespace Orkeon.Tools.Email;

/// <summary>Mapping shared by the e-mail tools.</summary>
internal static class EmailToolHelpers
{
    /// <summary>The <c>email_read</c>/<c>email_parser</c> view of <paramref name="message"/>, body sliced.</summary>
    public static EmailReadResponse BuildRead(
        MimeMessage message, EmailContentScreen screen, int? offset, int? maxChars,
        string? account = null, string? id = null, string? folder = null, bool? seen = null, bool? flagged = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(screen);

        var body = MimeMessageReader.ReadBody(message);
        var screening = screen.Screen(message.Subject, body.Text, body.HadHiddenContent);
        var text = screening.Withhold
            ? "[Body withheld: the prompt-injection screen rejected this message. An operator can lift Orkeon:Tools:Email:Screening:WithholdRejected.]"
            : body.Text;
        var (slice, start, next) = Slice(text, offset, maxChars);

        return new EmailReadResponse
        {
            Notice = EmailContentScreen.UntrustedNotice,
            Security = new EmailSecurityDto
            {
                Verdict = screening.Verdict,
                RiskScore = screening.RiskScore,
                Reasons = screening.Reasons,
                HiddenContent = screening.HiddenContent,
                Withheld = screening.Withhold,
            },
            Account = account,
            Id = id,
            Folder = folder,
            Seen = seen,
            Flagged = flagged,
            MessageId = message.MessageId,
            From = message.From.Mailboxes.Select(MimeMessageReader.Format).FirstOrDefault() ?? string.Empty,
            ReplyTo = MimeMessageReader.Format(message.ReplyTo),
            To = MimeMessageReader.Format(message.To),
            Cc = MimeMessageReader.Format(message.Cc),
            Date = MimeMessageReader.FormatDate(message),
            Subject = message.Subject ?? string.Empty,
            Attachments = MimeMessageReader.ReadAttachments(message).Select(ToDto).ToList(),
            TextOffset = start,
            TextLength = text.Length,
            NextOffset = next,
            Text = slice,
        };
    }

    /// <summary>The search-page view of <paramref name="summary"/>.</summary>
    public static EmailSummaryDto ToDto(MessageSummaryInfo summary, EmailContentScreen screen)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(screen);
        return new EmailSummaryDto
        {
            Id = summary.Id,
            From = summary.From,
            Subject = summary.Subject,
            Date = summary.Date?.ToString("O", CultureInfo.InvariantCulture),
            Seen = summary.Seen,
            Flagged = summary.Flagged,
            HasAttachments = summary.HasAttachments,
            Suspicious = screen.IsSuspicious(summary.Subject, summary.Preview),
            Preview = summary.Preview,
        };
    }

    /// <summary>The tool view of a folder.</summary>
    public static EmailFolderDto ToDto(MailFolderInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new EmailFolderDto
        {
            Path = folder.Path,
            Name = folder.Name,
            Role = folder.Role,
            Total = folder.Total,
            Unread = folder.Unread,
        };
    }

    /// <summary>The tool view of an attachment.</summary>
    public static EmailAttachmentDto ToDto(AttachmentEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new EmailAttachmentDto
        {
            Index = entry.Index,
            FileName = entry.FileName,
            ContentType = entry.ContentType,
            SizeBytes = entry.SizeBytes,
            Inline = entry.Inline,
        };
    }

    /// <summary>Reads a date argument (<c>YYYY-MM-DD</c> or ISO 8601), or throws a message that says the format.</summary>
    public static DateTimeOffset? ParseDate(string? value, string argument)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return date;
        throw new EmailToolException(EmailErrorCode.InvalidRequest, $"`{argument}` must be a date such as 2026-09-01 or an ISO 8601 date-time.");
    }

    /// <summary>Checks that <paramref name="ids"/> holds at least one id.</summary>
    public static IReadOnlyList<string> RequireIds(IReadOnlyList<string>? ids)
    {
        var list = ids?.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToList() ?? [];
        if (list.Count == 0)
            throw new EmailToolException(EmailErrorCode.InvalidRequest, "`ids` needs at least one message id from email_search.");
        return list;
    }

    private static (string Slice, int Start, int? Next) Slice(string text, int? offset, int? maxChars)
    {
        var size = Math.Clamp(maxChars ?? EmailDefaults.DefaultReadChars, 200, EmailDefaults.MaxReadChars);
        var start = Math.Clamp(offset ?? 0, 0, text.Length);
        var length = Math.Min(size, text.Length - start);
        var end = start + length;
        return (text.Substring(start, length), start, end < text.Length ? end : null);
    }
}
