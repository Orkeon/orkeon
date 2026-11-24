using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Chat.Dtos;

/// <summary>Request of <c>chat_list</c>.</summary>
internal sealed record ChatListRequest
{
    /// <summary>Account name; omit for the default account.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>What to list: teams, channels (of team) or chats; by default channels when team is given, else teams.</summary>
    [JsonPropertyName("kind")]
    [FieldSchema(Description = "What to list: teams, channels (of `team`) or chats; by default channels when `team` is given, else teams", IsRequired = false, Example = "chats")]
    public string? Kind { get; init; }

    /// <summary>The team whose channels are listed, by its name or its id.</summary>
    [JsonPropertyName("team")]
    [FieldSchema(Description = "The team whose channels are listed, by its name or its id", IsRequired = false)]
    public string? Team { get; init; }

    /// <summary>The most items a page may hold (default 20, at most 50); a page is also cut to what fits a tool result.</summary>
    [JsonPropertyName("limit")]
    [FieldSchema(Description = "The most items a page may hold (default 20, at most 50); a page is also cut to what fits a tool result", IsRequired = false)]
    public int? Limit { get; init; }

    /// <summary>next_cursor of the previous page, to read the next one with the same arguments.</summary>
    [JsonPropertyName("cursor")]
    [FieldSchema(Description = "`next_cursor` of the previous page, to read the next one with the same arguments", IsRequired = false)]
    public string? Cursor { get; init; }
}

/// <summary>A place.</summary>
internal sealed record ChatPlaceDto
{
    /// <summary>What to pass as place (a team has no messages of its own: list its channels).</summary>
    [JsonPropertyName("place")]
    [ReturnSchema(Description = "What to pass as `place` (a team has no messages of its own: list its channels)")]
    public string Place { get; init; } = "";

    /// <summary>Its id.</summary>
    [JsonPropertyName("id")]
    [ReturnSchema(Description = "Its id")]
    public string Id { get; init; } = "";

    /// <summary>team, channel, oneOnOne, group or meeting.</summary>
    [JsonPropertyName("kind")]
    [ReturnSchema(Description = "team, channel, oneOnOne, group or meeting")]
    public string Kind { get; init; } = "";

    /// <summary>Its name or topic: untrusted text.</summary>
    [JsonPropertyName("name")]
    [ReturnSchema(Description = "Its name or topic: untrusted text")]
    public string? Name { get; init; }

    /// <summary>For a chat, the names of some of its other members: untrusted text.</summary>
    [JsonPropertyName("members")]
    [ReturnSchema(Description = "For a chat, the names of some of its other members: untrusted text")]
    public IReadOnlyList<string>? Members { get; init; }

    /// <summary>Whether chat_read may read it, or a channel of it.</summary>
    [JsonPropertyName("can_read")]
    [ReturnSchema(Description = "Whether chat_read may read it, or a channel of it")]
    public bool CanRead { get; init; }

    /// <summary>Whether chat_send may post in it, or in a channel of it.</summary>
    [JsonPropertyName("can_send")]
    [ReturnSchema(Description = "Whether chat_send may post in it, or in a channel of it")]
    public bool CanSend { get; init; }

    /// <summary>Verdict of the prompt-injection screen on the names when not clean: suspicious or rejected.</summary>
    [JsonPropertyName("screening")]
    [ReturnSchema(Description = "Verdict of the prompt-injection screen on the names when not clean: suspicious or rejected")]
    public string? Screening { get; init; }
}

/// <summary>Response of <c>chat_list</c>.</summary>
internal sealed record ChatListResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>Reminder that the text was written by other people.</summary>
    [JsonPropertyName("notice")]
    [ReturnSchema(Description = "Reminder that the text was written by other people")]
    public string Notice { get; init; } = "";

    /// <summary>Places in this page.</summary>
    [JsonPropertyName("count")]
    [ReturnSchema(Description = "Places in this page")]
    public int Count { get; init; }

    /// <summary>Pass as cursor to read the next page; absent on the last page.</summary>
    [JsonPropertyName("next_cursor")]
    [ReturnSchema(Description = "Pass as `cursor` to read the next page; absent on the last page")]
    public string? NextCursor { get; init; }

    /// <summary>Allowed places the operator declared that match nothing, or several things.</summary>
    [JsonPropertyName("set_aside")]
    [ReturnSchema(Description = "Allowed places the operator declared that match nothing, or several things")]
    public IReadOnlyList<string>? SetAside { get; init; }

    /// <summary>The places the account may use.</summary>
    [JsonPropertyName("places")]
    [ReturnSchema(Description = "The places the account may use")]
    public IReadOnlyList<ChatPlaceDto> Places { get; init; } = [];
}

/// <summary>Request of <c>chat_members</c>.</summary>
internal sealed record ChatMembersRequest
{
    /// <summary>Account name; omit for the default account.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>Place: team:&lt;team&gt;/channel:&lt;channel&gt;, chat:&lt;id&gt; or user:&lt;address&gt;; a team or a channel by its name or its id; its members are listed.</summary>
    [JsonPropertyName("place")]
    [FieldSchema(Description = "Place: team:<team>/channel:<channel>, chat:<id> or user:<address>; a team or a channel by its name or its id; its members are listed", IsRequired = false)]
    public string? Place { get; init; }

    /// <summary>Instead of place: a person looked up in the directory by name, address or id.</summary>
    [JsonPropertyName("person")]
    [FieldSchema(Description = "Instead of `place`: a person looked up in the directory by name, address or id", IsRequired = false, Example = "ada@example.org")]
    public string? Person { get; init; }

    /// <summary>The most items a page may hold (default 20, at most 50); a page is also cut to what fits a tool result.</summary>
    [JsonPropertyName("limit")]
    [FieldSchema(Description = "The most items a page may hold (default 20, at most 50); a page is also cut to what fits a tool result", IsRequired = false)]
    public int? Limit { get; init; }

    /// <summary>next_cursor of the previous page, to read the next one with the same arguments.</summary>
    [JsonPropertyName("cursor")]
    [FieldSchema(Description = "`next_cursor` of the previous page, to read the next one with the same arguments", IsRequired = false)]
    public string? Cursor { get; init; }
}

/// <summary>A person.</summary>
internal sealed record ChatMemberDto
{
    /// <summary>The id to pass to chat_send in mentions, or as user:&lt;id&gt;.</summary>
    [JsonPropertyName("id")]
    [ReturnSchema(Description = "The id to pass to chat_send in `mentions`, or as user:<id>")]
    public string? Id { get; init; }

    /// <summary>Display name: untrusted text.</summary>
    [JsonPropertyName("name")]
    [ReturnSchema(Description = "Display name: untrusted text")]
    public string? Name { get; init; }

    /// <summary>Address, of the home tenant when it is known.</summary>
    [JsonPropertyName("address")]
    [ReturnSchema(Description = "Address, of the home tenant when it is known")]
    public string? Address { get; init; }

    /// <summary>Whether the person is a guest.</summary>
    [JsonPropertyName("guest")]
    [ReturnSchema(Description = "Whether the person is a guest")]
    public bool? Guest { get; init; }

    /// <summary>Whether the person owns the place.</summary>
    [JsonPropertyName("owner")]
    [ReturnSchema(Description = "Whether the person owns the place")]
    public bool? Owner { get; init; }

    /// <summary>Verdict of the prompt-injection screen on the name when not clean.</summary>
    [JsonPropertyName("screening")]
    [ReturnSchema(Description = "Verdict of the prompt-injection screen on the name when not clean")]
    public string? Screening { get; init; }
}

/// <summary>Response of <c>chat_members</c>.</summary>
internal sealed record ChatMembersResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>The place, by its ids.</summary>
    [JsonPropertyName("place")]
    [ReturnSchema(Description = "The place, by its ids")]
    public string? Place { get; init; }

    /// <summary>Reminder that the text was written by other people.</summary>
    [JsonPropertyName("notice")]
    [ReturnSchema(Description = "Reminder that the text was written by other people")]
    public string Notice { get; init; } = "";

    /// <summary>People in this page.</summary>
    [JsonPropertyName("count")]
    [ReturnSchema(Description = "People in this page")]
    public int Count { get; init; }

    /// <summary>Pass as cursor to read the next page; absent on the last page.</summary>
    [JsonPropertyName("next_cursor")]
    [ReturnSchema(Description = "Pass as `cursor` to read the next page; absent on the last page")]
    public string? NextCursor { get; init; }

    /// <summary>The members, or the people found.</summary>
    [JsonPropertyName("members")]
    [ReturnSchema(Description = "The members, or the people found")]
    public IReadOnlyList<ChatMemberDto> Members { get; init; } = [];
}
