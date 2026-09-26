using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Email.Dtos;

/// <summary>Request of <c>email_send</c> and <c>email_draft</c>.</summary>
internal sealed record EmailComposeRequest
{
    /// <summary>Account name.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>To.</summary>
    [JsonPropertyName("to")]
    [FieldSchema(Description = "Recipients, 'address' or 'Name <address>'; a reply defaults to the original sender", IsRequired = false)]
    public IReadOnlyList<string>? To { get; init; }

    /// <summary>Cc.</summary>
    [JsonPropertyName("cc")]
    [FieldSchema(Description = "Copy recipients", IsRequired = false)]
    public IReadOnlyList<string>? Cc { get; init; }

    /// <summary>Bcc.</summary>
    [JsonPropertyName("bcc")]
    [FieldSchema(Description = "Blind-copy recipients", IsRequired = false)]
    public IReadOnlyList<string>? Bcc { get; init; }

    /// <summary>Subject.</summary>
    [JsonPropertyName("subject")]
    [FieldSchema(Description = "Subject; required for a new message, derived (Re:/Fwd:) for a reply or a forward", IsRequired = false)]
    public string? Subject { get; init; }

    /// <summary>Plain-text body.</summary>
    [JsonPropertyName("text")]
    [FieldSchema(Description = "Plain-text body", IsRequired = false)]
    public string? Text { get; init; }

    /// <summary>HTML body.</summary>
    [JsonPropertyName("html")]
    [FieldSchema(Description = "Optional HTML body; a text version is derived when `text` is omitted", IsRequired = false)]
    public string? Html { get; init; }

    /// <summary>Files to attach.</summary>
    [JsonPropertyName("attachments")]
    [FieldSchema(Description = "Virtual paths of files to attach, e.g. /output/report.pdf", IsRequired = false)]
    public IReadOnlyList<string>? Attachments { get; init; }

    /// <summary>Message answered.</summary>
    [JsonPropertyName("reply_to_id")]
    [FieldSchema(Description = "Id of the message to reply to (threading headers and quote are added)", IsRequired = false)]
    public string? ReplyToId { get; init; }

    /// <summary>Reply to all.</summary>
    [JsonPropertyName("reply_all")]
    [FieldSchema(Description = "With reply_to_id: also address the original To and Cc", IsRequired = false)]
    public bool? ReplyAll { get; init; }

    /// <summary>Quote the original.</summary>
    [JsonPropertyName("quote_original")]
    [FieldSchema(Description = "With reply_to_id: quote the original text (default true)", IsRequired = false)]
    public bool? QuoteOriginal { get; init; }

    /// <summary>Message forwarded.</summary>
    [JsonPropertyName("forward_id")]
    [FieldSchema(Description = "Id of a message to forward, attached whole", IsRequired = false)]
    public string? ForwardId { get; init; }
}

/// <summary>Response of <c>email_draft</c>.</summary>
internal sealed record EmailDraftResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>Draft id.</summary>
    [JsonPropertyName("id")]
    [ReturnSchema(Description = "Id of the saved draft, when the server gives one")]
    public string? Id { get; init; }

    /// <summary>Drafts folder.</summary>
    [JsonPropertyName("folder")]
    [ReturnSchema(Description = "Folder the draft was saved in")]
    public string Folder { get; init; } = "";

    /// <summary>RFC Message-Id.</summary>
    [JsonPropertyName("message_id")]
    [ReturnSchema(Description = "RFC 5322 Message-Id of the draft")]
    public string MessageId { get; init; } = "";

    /// <summary>Recipients.</summary>
    [JsonPropertyName("recipients")]
    [ReturnSchema(Description = "Recipients the draft addresses")]
    public IReadOnlyList<string> Recipients { get; init; } = [];
}

/// <summary>Response of <c>email_send</c>.</summary>
internal sealed record EmailSendResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>RFC Message-Id.</summary>
    [JsonPropertyName("message_id")]
    [ReturnSchema(Description = "RFC 5322 Message-Id of the sent message")]
    public string MessageId { get; init; } = "";

    /// <summary>Recipients.</summary>
    [JsonPropertyName("recipients")]
    [ReturnSchema(Description = "Recipients the message was sent to")]
    public IReadOnlyList<string> Recipients { get; init; } = [];

    /// <summary>Time sent.</summary>
    [JsonPropertyName("sent_at")]
    [ReturnSchema(Description = "UTC time the server accepted the message, ISO 8601")]
    public string SentAt { get; init; } = "";

    /// <summary>What went wrong after the send, when something did.</summary>
    [JsonPropertyName("warning")]
    [ReturnSchema(Description = "Set when the message was sent but a follow-up step failed; do not send it again")]
    public string? Warning { get; init; }
}
