using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Chat.Dtos;

/// <summary>Request of <c>chat_read</c>.</summary>
internal sealed record ChatReadRequest
{
    /// <summary>Account name; omit for the default account.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Place: team:&lt;team&gt;/channel:&lt;channel&gt;, chat:&lt;id&gt; or user:&lt;address&gt;; a team or a channel by its name or its id.</summary>
    [JsonPropertyName("place")]
    [FieldSchema(Description = "Place: team:<team>/channel:<channel>, chat:<id> or user:<address>; a team or a channel by its name or its id", IsRequired = true, Example = "team:Platform/channel:builds")]
    public string Place { get; init; } = "";

    /// <summary>In a channel: read the replies of this message instead of the channel's messages.</summary>
    [JsonPropertyName("message_id")]
    [FieldSchema(Description = "In a channel: read the replies of this message instead of the channel's messages", IsRequired = false)]
    public string? MessageId { get; init; }

    /// <summary>Only messages posted at or after this date (YYYY-MM-DD or ISO 8601, UTC by default).</summary>
    [JsonPropertyName("since")]
    [FieldSchema(Description = "Only messages posted at or after this date (YYYY-MM-DD or ISO 8601, UTC by default)", IsRequired = false)]
    public string? Since { get; init; }

    /// <summary>Only messages posted before this date (YYYY-MM-DD or ISO 8601, UTC by default).</summary>
    [JsonPropertyName("until")]
    [FieldSchema(Description = "Only messages posted before this date (YYYY-MM-DD or ISO 8601, UTC by default)", IsRequired = false)]
    public string? Until { get; init; }

    /// <summary>The most characters of each message's text (default 1000).</summary>
    [JsonPropertyName("text_chars")]
    [FieldSchema(Description = "The most characters of each message's text (default 1000)", IsRequired = false)]
    public int? TextChars { get; init; }

    /// <summary>The most messages a page may hold (default 20, at most 50); a page is also cut to what fits a tool result.</summary>
    [JsonPropertyName("limit")]
    [FieldSchema(Description = "The most messages a page may hold (default 20, at most 50); a page is also cut to what fits a tool result", IsRequired = false)]
    public int? Limit { get; init; }

    /// <summary>next_cursor of the previous page, to read the next one with the same arguments.</summary>
    [JsonPropertyName("cursor")]
    [FieldSchema(Description = "`next_cursor` of the previous page, to read the next one with the same arguments", IsRequired = false)]
    public string? Cursor { get; init; }
}

/// <summary>Who wrote a message.</summary>
internal sealed record ChatAuthorDto
{
    /// <summary>User or application id.</summary>
    [JsonPropertyName("id")]
    [ReturnSchema(Description = "User or application id")]
    public string? Id { get; init; }

    /// <summary>Display name: untrusted text.</summary>
    [JsonPropertyName("name")]
    [ReturnSchema(Description = "Display name: untrusted text")]
    public string? Name { get; init; }

    /// <summary>Address, when the directory knows the person.</summary>
    [JsonPropertyName("address")]
    [ReturnSchema(Description = "Address, when the directory knows the person")]
    public string? Address { get; init; }

    /// <summary>Whether a guest, or someone outside the tenant, wrote it.</summary>
    [JsonPropertyName("guest")]
    [ReturnSchema(Description = "Whether a guest, or someone outside the tenant, wrote it")]
    public bool? Guest { get; init; }

    /// <summary>Whether an application wrote it.</summary>
    [JsonPropertyName("application")]
    [ReturnSchema(Description = "Whether an application wrote it")]
    public bool? Application { get; init; }
}

/// <summary>A person a message mentions.</summary>
internal sealed record ChatMentionDto
{
    /// <summary>User id.</summary>
    [JsonPropertyName("id")]
    [ReturnSchema(Description = "User id")]
    public string? Id { get; init; }

    /// <summary>Name shown: untrusted text.</summary>
    [JsonPropertyName("name")]
    [ReturnSchema(Description = "Name shown: untrusted text")]
    public string? Name { get; init; }
}

/// <summary>A message.</summary>
internal sealed record ChatMessageDto
{
    /// <summary>Id, to pass as message_id or to chat_send as reply_to.</summary>
    [JsonPropertyName("id")]
    [ReturnSchema(Description = "Id, to pass as `message_id` or to chat_send as `reply_to`")]
    public string Id { get; init; } = "";

    /// <summary>Who wrote it.</summary>
    [JsonPropertyName("author")]
    [ReturnSchema(Description = "Who wrote it")]
    public ChatAuthorDto? Author { get; init; }

    /// <summary>When it was posted (UTC).</summary>
    [JsonPropertyName("time")]
    [ReturnSchema(Description = "When it was posted (UTC)")]
    public string? Time { get; init; }

    /// <summary>When it was last edited (UTC).</summary>
    [JsonPropertyName("edited")]
    [ReturnSchema(Description = "When it was last edited (UTC)")]
    public string? Edited { get; init; }

    /// <summary>Its text: untrusted; absent when withheld.</summary>
    [JsonPropertyName("text")]
    [ReturnSchema(Description = "Its text: untrusted; absent when withheld")]
    public string? Text { get; init; }

    /// <summary>Whether the text goes on: raise text_chars.</summary>
    [JsonPropertyName("text_truncated")]
    [ReturnSchema(Description = "Whether the text goes on: raise `text_chars`")]
    public bool? TextTruncated { get; init; }

    /// <summary>Whether the text is withheld.</summary>
    [JsonPropertyName("withheld")]
    [ReturnSchema(Description = "Whether the text is withheld")]
    public bool? Withheld { get; init; }

    /// <summary>Why it is withheld.</summary>
    [JsonPropertyName("reason")]
    [ReturnSchema(Description = "Why it is withheld")]
    public string? Reason { get; init; }

    /// <summary>Verdict of the prompt-injection screen when not clean: suspicious or rejected.</summary>
    [JsonPropertyName("screening")]
    [ReturnSchema(Description = "Verdict of the prompt-injection screen when not clean: suspicious or rejected")]
    public string? Screening { get; init; }

    /// <summary>Whether the body held text a reader does not see; it is left out.</summary>
    [JsonPropertyName("hidden_text")]
    [ReturnSchema(Description = "Whether the body held text a reader does not see; it is left out")]
    public bool? HiddenText { get; init; }

    /// <summary>The people it mentions.</summary>
    [JsonPropertyName("mentions")]
    [ReturnSchema(Description = "The people it mentions")]
    public IReadOnlyList<ChatMentionDto>? Mentions { get; init; }

    /// <summary>Its reactions, counted by kind.</summary>
    [JsonPropertyName("reactions")]
    [ReturnSchema(Description = "Its reactions, counted by kind")]
    public IReadOnlyDictionary<string, int>? Reactions { get; init; }

    /// <summary>The names of its attachments: untrusted text.</summary>
    [JsonPropertyName("attachments")]
    [ReturnSchema(Description = "The names of its attachments: untrusted text")]
    public IReadOnlyList<string>? Attachments { get; init; }

    /// <summary>In a channel, how many replies it has.</summary>
    [JsonPropertyName("replies")]
    [ReturnSchema(Description = "In a channel, how many replies it has")]
    public int? Replies { get; init; }

    /// <summary>Whether it has more replies than counted.</summary>
    [JsonPropertyName("more_replies")]
    [ReturnSchema(Description = "Whether it has more replies than counted")]
    public bool? MoreReplies { get; init; }
}

/// <summary>Response of <c>chat_read</c>.</summary>
internal sealed record ChatReadResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>The place of these messages, by its ids.</summary>
    [JsonPropertyName("place")]
    [ReturnSchema(Description = "The place of these messages, by its ids")]
    public string Place { get; init; } = "";

    /// <summary>The message these are replies of.</summary>
    [JsonPropertyName("thread")]
    [ReturnSchema(Description = "The message these are replies of")]
    public string? Thread { get; init; }

    /// <summary>Reminder that the text was written by other people.</summary>
    [JsonPropertyName("notice")]
    [ReturnSchema(Description = "Reminder that the text was written by other people")]
    public string Notice { get; init; } = "";

    /// <summary>Messages in this page.</summary>
    [JsonPropertyName("count")]
    [ReturnSchema(Description = "Messages in this page")]
    public int Count { get; init; }

    /// <summary>Pass as cursor to go on; a page may be empty and have a next one, when no message yet fell inside the dates.</summary>
    [JsonPropertyName("next_cursor")]
    [ReturnSchema(Description = "Pass as `cursor` to go on; a page may be empty and have a next one, when no message yet fell inside the dates")]
    public string? NextCursor { get; init; }

    /// <summary>The messages, newest first (a channel's threads by their last activity).</summary>
    [JsonPropertyName("messages")]
    [ReturnSchema(Description = "The messages, newest first (a channel's threads by their last activity)")]
    public IReadOnlyList<ChatMessageDto> Messages { get; init; } = [];
}

/// <summary>Request of <c>chat_send</c>.</summary>
internal sealed record ChatSendRequest
{
    /// <summary>Account name; omit for the default account.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Place: team:&lt;team&gt;/channel:&lt;channel&gt;, chat:&lt;id&gt; or user:&lt;address&gt;; a team or a channel by its name or its id. The one-to-one chat with a person is opened when there is none. Required, except on a webhook account, which takes none.</summary>
    [JsonPropertyName("place")]
    [FieldSchema(Description = "Place: team:<team>/channel:<channel>, chat:<id> or user:<address>; a team or a channel by its name or its id. The one-to-one chat with a person is opened when there is none. Required, except on an account whose provider is TeamsWebhook, which takes none", IsRequired = false, Example = "user:ada@example.org")]
    public string? Place { get; init; }

    /// <summary>The message.</summary>
    [JsonPropertyName("text")]
    [FieldSchema(Description = "The message", IsRequired = true)]
    public string Text { get; init; } = "";

    /// <summary>markdown (default) or text; HTML is shown as written, never rendered.</summary>
    [JsonPropertyName("format")]
    [FieldSchema(Description = "markdown (default) or text; HTML is shown as written, never rendered", IsRequired = false, Example = "markdown")]
    public string? Format { get; init; }

    /// <summary>Ids of the people to mention, from chat_members; they are named ahead of the text.</summary>
    [JsonPropertyName("mentions")]
    [FieldSchema(Description = "Ids of the people to mention, from chat_members; they are named ahead of the text", IsRequired = false)]
    public IReadOnlyList<string>? Mentions { get; init; }

    /// <summary>Id of the message to reply to: in its thread in a channel, quoting it in a chat.</summary>
    [JsonPropertyName("reply_to")]
    [FieldSchema(Description = "Id of the message to reply to: in its thread in a channel, quoting it in a chat", IsRequired = false)]
    public string? ReplyTo { get; init; }
}

/// <summary>Response of <c>chat_send</c>.</summary>
internal sealed record ChatSendResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>Where it was posted, by its ids; absent for a webhook.</summary>
    [JsonPropertyName("place")]
    [ReturnSchema(Description = "Where it was posted, by its ids; absent for a webhook")]
    public string? Place { get; init; }

    /// <summary>Id of the message posted.</summary>
    [JsonPropertyName("message_id")]
    [ReturnSchema(Description = "Id of the message posted")]
    public string? MessageId { get; init; }

    /// <summary>When it was posted (UTC).</summary>
    [JsonPropertyName("sent_at")]
    [ReturnSchema(Description = "When it was posted (UTC)")]
    public string? SentAt { get; init; }

    /// <summary>What to know though the message was posted.</summary>
    [JsonPropertyName("warning")]
    [ReturnSchema(Description = "What to know though the message was posted")]
    public string? Warning { get; init; }
}
