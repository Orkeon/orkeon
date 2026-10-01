using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.EventHub.Dtos;

/// <summary>Request for the <c>post_message</c> tool.</summary>
public sealed record PostMessageRequest
{
    /// <summary>Target mailbox URI (<c>agent://</c>, <c>crew://</c>, <c>topic://</c>, or <c>client://</c>).</summary>
    [JsonPropertyName("target_mailbox")]
    [FieldSchema(Description = "Mailbox URI (agent://, crew://, topic://, or client:// for an external client such as Studio)", Example = "agent://crew-1/agent-7")]
    public string TargetMailbox { get; init; } = "";

    /// <summary>JSON payload carried by the message.</summary>
    [JsonPropertyName("payload")]
    [FieldSchema(Description = "JSON payload", Type = "any", IsRequired = false)]
    public JsonNode? Payload { get; init; }

    /// <summary>Optional metadata copied verbatim into the envelope the recipient reads.</summary>
    [JsonPropertyName("metadata")]
    [FieldSchema(Description = "Optional metadata key/value pairs, copied into the envelope the recipient reads", IsRequired = false)]
    public ImmutableDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Response for the <c>post_message</c> tool.</summary>
public sealed record PostMessageResponse
{
    /// <summary>The id the hub gave the posted message — the <c>message_id</c> the recipient reads.</summary>
    [JsonPropertyName("message_id")]
    [ReturnSchema(Description = "Id of the posted message, as the recipient's receive_message reads it")]
    public string MessageId { get; init; } = "";

    /// <summary>UTC timestamp at which the post was accepted by the hub.</summary>
    [JsonPropertyName("posted_at")]
    [ReturnSchema(Description = "UTC post timestamp (ISO-8601)")]
    public string PostedAt { get; init; } = "";
}
