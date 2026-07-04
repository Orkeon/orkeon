using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.EventHub.Dtos;

/// <summary>Request for the <c>post_message</c> tool.</summary>
public sealed record PostMessageRequest
{
    /// <summary>Target mailbox URI (<c>agent://</c>, <c>crew://</c>, or <c>topic://</c>).</summary>
    [JsonPropertyName("target_mailbox")]
    [FieldSchema(Description = "Mailbox URI (agent://, crew://, or topic://)", Example = "agent://crew-1/agent-7")]
    public string TargetMailbox { get; init; } = "";

    /// <summary>JSON payload carried by the message.</summary>
    [JsonPropertyName("payload")]
    [FieldSchema(Description = "JSON payload", Type = "any", IsRequired = false)]
    public JsonNode? Payload { get; init; }

    /// <summary>Optional metadata copied verbatim into the envelope.</summary>
    [JsonPropertyName("metadata")]
    [FieldSchema(Description = "Optional metadata key/value pairs", IsRequired = false)]
    public ImmutableDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Response for the <c>post_message</c> tool.</summary>
public sealed record PostMessageResponse
{
    /// <summary>The unique id assigned to the posted message.</summary>
    [JsonPropertyName("message_id")]
    [ReturnSchema(Description = "Unique id of the posted message")]
    public string MessageId { get; init; } = "";

    /// <summary>UTC timestamp at which the post was accepted by the hub.</summary>
    [JsonPropertyName("posted_at")]
    [ReturnSchema(Description = "UTC post timestamp (ISO-8601)")]
    public string PostedAt { get; init; } = "";
}
