using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MimeKit;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Tools.Email.Constants;
using Orkeon.Tools.Email.Dtos;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Mime;
using Orkeon.Tools.Email.Security;

namespace Orkeon.Tools.Email;

/// <summary>
/// Where a read message comes from, as <see cref="EmailToolHelpers.BuildRead"/> reports it: the
/// account and folder it was fetched from, its id and flags — all absent for a parsed file.
/// </summary>
internal sealed record EmailReadOrigin
{
    /// <summary>An origin that names nothing: a message parsed from a file.</summary>
    public static EmailReadOrigin None { get; } = new();

    public string? Account { get; init; }
    public string? Id { get; init; }
    public string? Folder { get; init; }
    public bool? Seen { get; init; }
    public bool? Flagged { get; init; }
}

/// <summary>Mapping shared by the e-mail tools.</summary>
internal static class EmailToolHelpers
{
    /// <summary>Room kept free under the agent loop's cap, for what its rendering may add.</summary>
    private const int RenderingMargin = 200;

    /// <summary>The least body text a read returns, whatever the headers take, so that reading always advances.</summary>
    private const int MinTextWidth = 300;

    /// <summary>How many addresses a list keeps when the headers alone would crowd the body out.</summary>
    private const int CrowdedAddressList = 5;

    /// <summary>The longest subject or sender a search result shows.</summary>
    private const int MaxSummaryField = 200;

    /// <summary>
    /// A result as the agent loop renders it: compact JSON, relaxed escaping, and no null
    /// member (the tool pipeline drops them). Measured with it, a result is cut where the tool
    /// decides — at a message, at a body offset — with a cursor or an offset that resumes
    /// exactly there, instead of where the loop truncates it, after the cursor it already sent.
    /// </summary>
    private static readonly JsonSerializerOptions Rendering = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The characters a result of <paramref name="toolName"/> may take: what the agent loop keeps of it, less a margin.</summary>
    public static int ResultBudget(string toolName) => AgentDefaults.ResolveMaxToolResultLength(toolName) - RenderingMargin;

    /// <summary>The length of <paramref name="result"/> as the agent loop renders it.</summary>
    public static int RenderedLength<T>(T result) => JsonSerializer.Serialize(result, Rendering).Length;

    /// <summary>
    /// The <c>email_read</c>/<c>email_parser</c> view of <paramref name="message"/>: the body
    /// sliced to at most <paramref name="maxChars"/> characters, and further to what fits in
    /// <paramref name="budget"/> once the headers are counted — <c>next_offset</c> then resumes
    /// exactly where the slice ends.
    /// </summary>
    public static EmailReadResponse BuildRead(
        MimeMessage message, EmailContentScreen screen, int? offset, int? maxChars, int budget, EmailReadOrigin? origin = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(screen);
        origin ??= EmailReadOrigin.None;

        var body = MimeMessageReader.ReadBody(message);
        var screening = screen.Screen(message.Subject, body.Text, body.HadHiddenContent);
        var text = screening.Withhold
            ? "[Body withheld: the prompt-injection screen rejected this message. An operator can lift Orkeon:Tools:Email:Screening:WithholdRejected.]"
            : body.Text;
        var start = Math.Clamp(offset ?? 0, 0, text.Length);
        var maxLength = Math.Clamp(maxChars ?? EmailDefaults.DefaultReadChars, 200, EmailDefaults.MaxReadChars);

        var replyTo = MimeMessageReader.Format(message.ReplyTo);
        var to = MimeMessageReader.Format(message.To);
        var cc = MimeMessageReader.Format(message.Cc);
        var shell = new EmailReadResponse
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
            Account = origin.Account,
            Id = origin.Id,
            Folder = origin.Folder,
            Seen = origin.Seen,
            Flagged = origin.Flagged,
            MessageId = message.MessageId,
            From = message.From.Mailboxes.Select(MimeMessageReader.Format).FirstOrDefault() ?? string.Empty,
            ReplyTo = replyTo,
            To = to,
            Cc = cc,
            Date = MimeMessageReader.FormatDate(message),
            Subject = message.Subject ?? string.Empty,
            Attachments = MimeMessageReader.ReadAttachments(message).Select(ToDto).ToList(),
            TextOffset = start,
            TextLength = text.Length,
            NextOffset = text.Length,
            Text = string.Empty,
        };

        // Everything but the body, measured with the widest next_offset it can carry. Headers
        // that would leave the body no room (a message copied to hundreds) keep their first
        // addresses and say how many they leave out.
        var overhead = RenderedLength(shell);
        if (overhead > budget - MinTextWidth)
        {
            shell = shell with { ReplyTo = Crowded(replyTo), To = Crowded(to), Cc = Crowded(cc) };
            overhead = RenderedLength(shell);
        }

        var length = FitText(text, start, maxLength, Math.Max(budget - overhead, MinTextWidth));
        var end = start + length;
        return shell with
        {
            NextOffset = end < text.Length ? end : null,
            Text = text.Substring(start, length),
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
            From = Clip(summary.From),
            Subject = Clip(summary.Subject),
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
            AlsoRoles = folder.AlsoRoles,
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

    /// <summary>
    /// The longest slice of <paramref name="text"/> from <paramref name="start"/>, at most
    /// <paramref name="maxLength"/> characters, whose rendering (quotes and line breaks escaped)
    /// takes at most <paramref name="width"/> characters. A surrogate pair is never split.
    /// </summary>
    private static int FitText(string text, int start, int maxLength, int width)
    {
        var low = 0;
        var high = Math.Min(maxLength, text.Length - start);
        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);
            if (JsonSerializer.Serialize(text.Substring(start, middle), Rendering).Length - 2 <= width)
                low = middle;
            else
                high = middle - 1;
        }

        if (low > 0 && char.IsHighSurrogate(text[start + low - 1]))
            low--;
        return low;
    }

    /// <summary>The first addresses of <paramref name="addresses"/>, then how many were left out.</summary>
    private static IReadOnlyList<string> Crowded(IReadOnlyList<string> addresses) =>
        addresses.Count <= CrowdedAddressList
            ? addresses
            : [.. addresses.Take(CrowdedAddressList), string.Create(CultureInfo.InvariantCulture, $"(+{addresses.Count - CrowdedAddressList} more)")];

    private static string Clip(string value) =>
        value.Length <= MaxSummaryField ? value : string.Concat(value.AsSpan(0, MaxSummaryField - 1), "…");
}
