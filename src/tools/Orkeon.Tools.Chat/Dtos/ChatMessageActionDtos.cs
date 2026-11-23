using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Chat.Dtos;

/// <summary>Request of <c>chat_search</c>.</summary>
internal sealed record ChatSearchRequest
{
    /// <summary>Account name; omit for the default account.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Words the message holds, all of them, each taken literally: no operator and no wildcard.</summary>
    [JsonPropertyName("text")]
    [FieldSchema(Description = "Words the message holds, all of them, each taken literally: no operator and no wildcard", IsRequired = false)]
    public string? Text { get; init; }

    /// <summary>Only messages by this person: an address, or an id from chat_members.</summary>
    [JsonPropertyName("author")]
    [FieldSchema(Description = "Only messages by this person: an address, or an id from chat_members", IsRequired = false)]
    public string? Author { get; init; }

    /// <summary>Only messages that mention the signed-in user.</summary>
    [JsonPropertyName("mentions_me")]
    [FieldSchema(Description = "Only messages that mention the signed-in user", IsRequired = false)]
    public bool? MentionsMe { get; init; }

    /// <summary>The most characters of each message's text (default 1000).</summary>
    [JsonPropertyName("text_chars")]
    [FieldSchema(Description = "The most characters of each message's text (default 1000)", IsRequired = false)]
    public int? TextChars { get; init; }

    /// <summary>The most messages a page may hold (default 10, at most 25); a page is also cut to what fits a tool result.</summary>
    [JsonPropertyName("limit")]
    [FieldSchema(Description = "The most messages a page may hold (default 10, at most 25); a page is also cut to what fits a tool result", IsRequired = false)]
    public int? Limit { get; init; }

    /// <summary>next_cursor of the previous page, to read the next one with the same arguments.</summary>
    [JsonPropertyName("cursor")]
    [FieldSchema(Description = "`next_cursor` of the previous page, to read the next one with the same arguments", IsRequired = false)]
    public string? Cursor { get; init; }
}

/// <summary>A message a search found.</summary>
internal sealed record ChatSearchHitDto
{
    /// <summary>Where it is, by its ids.</summary>
    [JsonPropertyName("place")]
    [ReturnSchema(Description = "Where it is, by its ids")]
    public string Place { get; init; } = "";

    /// <summary>In a channel, the message it replies to.</summary>
    [JsonPropertyName("thread")]
    [ReturnSchema(Description = "In a channel, the message it replies to")]
    public string? Thread { get; init; }

    /// <summary>The message.</summary>
    [JsonPropertyName("message")]
    [ReturnSchema(Description = "The message")]
    public ChatMessageDto Message { get; init; } = new();
}

/// <summary>Response of <c>chat_search</c>.</summary>
internal sealed record ChatSearchResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>Reminder that the text was written by other people.</summary>
    [JsonPropertyName("notice")]
    [ReturnSchema(Description = "Reminder that the text was written by other people")]
    public string Notice { get; init; } = "";

    /// <summary>What the search left out.</summary>
    [JsonPropertyName("limits")]
    [ReturnSchema(Description = "What the search left out")]
    public string? Limits { get; init; }

    /// <summary>Messages in this page.</summary>
    [JsonPropertyName("count")]
    [ReturnSchema(Description = "Messages in this page")]
    public int Count { get; init; }

    /// <summary>Pass as cursor to go on.</summary>
    [JsonPropertyName("next_cursor")]
    [ReturnSchema(Description = "Pass as `cursor` to go on")]
    public string? NextCursor { get; init; }

    /// <summary>The messages, as the service ranks them.</summary>
    [JsonPropertyName("hits")]
    [ReturnSchema(Description = "The messages, as the service ranks them")]
    public IReadOnlyList<ChatSearchHitDto> Hits { get; init; } = [];
}

/// <summary>Request of <c>chat_edit</c>.</summary>
internal sealed record ChatEditRequest
{
    /// <summary>Account name; omit for the default account.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Place of the message: team:&lt;team&gt;/channel:&lt;channel&gt;, chat:&lt;id&gt; or user:&lt;address&gt;; a team or a channel by its name or its id.</summary>
    [JsonPropertyName("place")]
    [FieldSchema(Description = "Place of the message: team:<team>/channel:<channel>, chat:<id> or user:<address>; a team or a channel by its name or its id", IsRequired = true)]
    public string Place { get; init; } = "";

    /// <summary>Id of the message.</summary>
    [JsonPropertyName("message_id")]
    [FieldSchema(Description = "Id of the message", IsRequired = true)]
    public string MessageId { get; init; } = "";

    /// <summary>For a reply in a channel: id of the message it replies to.</summary>
    [JsonPropertyName("thread")]
    [FieldSchema(Description = "For a reply in a channel: id of the message it replies to", IsRequired = false)]
    public string? Thread { get; init; }

    /// <summary>The whole text that replaces it.</summary>
    [JsonPropertyName("text")]
    [FieldSchema(Description = "The whole text that replaces it", IsRequired = true)]
    public string Text { get; init; } = "";

    /// <summary>markdown (default) or text; HTML is shown as written, never rendered.</summary>
    [JsonPropertyName("format")]
    [FieldSchema(Description = "markdown (default) or text; HTML is shown as written, never rendered", IsRequired = false)]
    public string? Format { get; init; }

    /// <summary>Ids of the people to mention, from chat_members; those of the previous text are dropped.</summary>
    [JsonPropertyName("mentions")]
    [FieldSchema(Description = "Ids of the people to mention, from chat_members; those of the previous text are dropped", IsRequired = false)]
    public IReadOnlyList<string>? Mentions { get; init; }
}

/// <summary>Request of <c>chat_delete</c>.</summary>
internal sealed record ChatDeleteRequest
{
    /// <summary>Account name; omit for the default account.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Place of the message: team:&lt;team&gt;/channel:&lt;channel&gt;, chat:&lt;id&gt; or user:&lt;address&gt;; a team or a channel by its name or its id.</summary>
    [JsonPropertyName("place")]
    [FieldSchema(Description = "Place of the message: team:<team>/channel:<channel>, chat:<id> or user:<address>; a team or a channel by its name or its id", IsRequired = true)]
    public string Place { get; init; } = "";

    /// <summary>Id of the message.</summary>
    [JsonPropertyName("message_id")]
    [FieldSchema(Description = "Id of the message", IsRequired = true)]
    public string MessageId { get; init; } = "";

    /// <summary>For a reply in a channel: id of the message it replies to.</summary>
    [JsonPropertyName("thread")]
    [FieldSchema(Description = "For a reply in a channel: id of the message it replies to", IsRequired = false)]
    public string? Thread { get; init; }
}

/// <summary>Response of <c>chat_edit</c> and <c>chat_delete</c>.</summary>
internal sealed record ChatMessageWriteResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>Where the message is, by its ids.</summary>
    [JsonPropertyName("place")]
    [ReturnSchema(Description = "Where the message is, by its ids")]
    public string Place { get; init; } = "";

    /// <summary>Id of the message changed.</summary>
    [JsonPropertyName("message_id")]
    [ReturnSchema(Description = "Id of the message changed")]
    public string MessageId { get; init; } = "";
}

/// <summary>Request of <c>chat_react</c>.</summary>
internal sealed record ChatReactRequest
{
    /// <summary>Account name; omit for the default account.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Chat of the message: chat:&lt;id&gt; or user:&lt;address&gt;.</summary>
    [JsonPropertyName("place")]
    [FieldSchema(Description = "Chat of the message: chat:<id> or user:<address>", IsRequired = true)]
    public string Place { get; init; } = "";

    /// <summary>Id of the message.</summary>
    [JsonPropertyName("message_id")]
    [FieldSchema(Description = "Id of the message", IsRequired = true)]
    public string MessageId { get; init; } = "";

    /// <summary>One emoji character.</summary>
    [JsonPropertyName("reaction")]
    [FieldSchema(Description = "One emoji character", IsRequired = true)]
    public string Reaction { get; init; } = "";

    /// <summary>Remove the reaction instead of setting it.</summary>
    [JsonPropertyName("remove")]
    [FieldSchema(Description = "Remove the reaction instead of setting it", IsRequired = false)]
    public bool? Remove { get; init; }
}

/// <summary>Response of <c>chat_react</c>.</summary>
internal sealed record ChatReactResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>Where the message is, by its ids.</summary>
    [JsonPropertyName("place")]
    [ReturnSchema(Description = "Where the message is, by its ids")]
    public string Place { get; init; } = "";

    /// <summary>Id of the message.</summary>
    [JsonPropertyName("message_id")]
    [ReturnSchema(Description = "Id of the message")]
    public string MessageId { get; init; } = "";

    /// <summary>The reaction.</summary>
    [JsonPropertyName("reaction")]
    [ReturnSchema(Description = "The reaction")]
    public string Reaction { get; init; } = "";

    /// <summary>Whether it was removed.</summary>
    [JsonPropertyName("removed")]
    [ReturnSchema(Description = "Whether it was removed")]
    public bool? Removed { get; init; }
}
