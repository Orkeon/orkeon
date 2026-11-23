using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.Chat.Dtos;

/// <summary>Request of <c>chat_meetings</c>.</summary>
internal sealed record ChatMeetingsRequest
{
    /// <summary>Account name; omit for the default account.</summary>
    [JsonPropertyName("account")]
    [FieldSchema(Description = "Account name; omit for the default account", IsRequired = false)]
    public string? Account { get; init; }

    /// <summary>The chat of the meeting, chat:&lt;id&gt;, from chat_list; or pass join_link.</summary>
    [JsonPropertyName("place")]
    [FieldSchema(Description = "The chat of the meeting, chat:<id>, from chat_list; or pass `join_link`", IsRequired = false)]
    public string? Place { get; init; }

    /// <summary>The join link of the meeting, as its invitation holds it; or pass place.</summary>
    [JsonPropertyName("join_link")]
    [FieldSchema(Description = "The join link of the meeting, as its invitation holds it; or pass `place`", IsRequired = false)]
    public string? JoinLink { get; init; }

    /// <summary>Read the text of this transcript; omit to list them.</summary>
    [JsonPropertyName("transcript_id")]
    [FieldSchema(Description = "Read the text of this transcript; omit to list them", IsRequired = false)]
    public string? TranscriptId { get; init; }

    /// <summary>next_cursor of the previous page, to read the next one with the same arguments.</summary>
    [JsonPropertyName("cursor")]
    [FieldSchema(Description = "`next_cursor` of the previous page, to read the next one with the same arguments", IsRequired = false)]
    public string? Cursor { get; init; }
}

/// <summary>A transcript of a meeting.</summary>
internal sealed record ChatTranscriptDto
{
    /// <summary>What to pass as transcript_id.</summary>
    [JsonPropertyName("id")]
    [ReturnSchema(Description = "What to pass as `transcript_id`")]
    public string Id { get; init; } = "";

    /// <summary>When it begins, ISO 8601 UTC.</summary>
    [JsonPropertyName("created")]
    [ReturnSchema(Description = "When it begins, ISO 8601 UTC")]
    public string? Created { get; init; }

    /// <summary>When it ends, ISO 8601 UTC.</summary>
    [JsonPropertyName("ended")]
    [ReturnSchema(Description = "When it ends, ISO 8601 UTC")]
    public string? Ended { get; init; }
}

/// <summary>Response of <c>chat_meetings</c>.</summary>
internal sealed record ChatMeetingsResponse
{
    /// <summary>Account used.</summary>
    [JsonPropertyName("account")]
    [ReturnSchema(Description = "Account used")]
    public string Account { get; init; } = "";

    /// <summary>The chat of the meeting, by its id.</summary>
    [JsonPropertyName("place")]
    [ReturnSchema(Description = "The chat of the meeting, by its id")]
    public string Place { get; init; } = "";

    /// <summary>Reminder that the text was said by other people.</summary>
    [JsonPropertyName("notice")]
    [ReturnSchema(Description = "Reminder that the text was said by other people")]
    public string Notice { get; init; } = "";

    /// <summary>Subject of the meeting.</summary>
    [JsonPropertyName("subject")]
    [ReturnSchema(Description = "Subject of the meeting")]
    public string? Subject { get; init; }

    /// <summary>When the meeting starts, ISO 8601 UTC.</summary>
    [JsonPropertyName("start")]
    [ReturnSchema(Description = "When the meeting starts, ISO 8601 UTC")]
    public string? Start { get; init; }

    /// <summary>Its transcripts; absent when one is read.</summary>
    [JsonPropertyName("transcripts")]
    [ReturnSchema(Description = "Its transcripts; absent when one is read")]
    public IReadOnlyList<ChatTranscriptDto>? Transcripts { get; init; }

    /// <summary>The transcript read.</summary>
    [JsonPropertyName("transcript_id")]
    [ReturnSchema(Description = "The transcript read")]
    public string? TranscriptId { get; init; }

    /// <summary>One page of its text, a line per speech: [time] speaker: words.</summary>
    [JsonPropertyName("text")]
    [ReturnSchema(Description = "One page of its text, a line per speech: [time] speaker: words")]
    public string? Text { get; init; }

    /// <summary>The tenant hides who speaks.</summary>
    [JsonPropertyName("no_speakers")]
    [ReturnSchema(Description = "The tenant hides who speaks")]
    public bool? NoSpeakers { get; init; }

    /// <summary>Whether the text is withheld.</summary>
    [JsonPropertyName("withheld")]
    [ReturnSchema(Description = "Whether the text is withheld")]
    public bool? Withheld { get; init; }

    /// <summary>Why it is withheld.</summary>
    [JsonPropertyName("reason")]
    [ReturnSchema(Description = "Why it is withheld")]
    public string? Reason { get; init; }

    /// <summary>Prompt-injection verdict of the subject and the whole text, when not clean.</summary>
    [JsonPropertyName("screening")]
    [ReturnSchema(Description = "Prompt-injection verdict of the subject and the whole text, when not clean")]
    public string? Screening { get; init; }

    /// <summary>Pass as cursor to go on.</summary>
    [JsonPropertyName("next_cursor")]
    [ReturnSchema(Description = "Pass as `cursor` to go on")]
    public string? NextCursor { get; init; }
}
