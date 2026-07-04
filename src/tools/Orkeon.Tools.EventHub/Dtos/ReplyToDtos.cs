using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.EventHub.Dtos;

/// <summary>Request for the <c>reply_to</c> tool.</summary>
public sealed record ReplyToRequest
{
    /// <summary>Correlation identifier obtained from the original request envelope.</summary>
    [JsonPropertyName("correlation_id")]
    [FieldSchema(Description = "Correlation id from the original Send/request envelope")]
    public string CorrelationId { get; init; } = "";

    /// <summary>JSON payload returned to the requester.</summary>
    [JsonPropertyName("payload")]
    [FieldSchema(Description = "JSON payload to return", Type = "any", IsRequired = false)]
    public JsonNode? Payload { get; init; }
}

/// <summary>Response for the <c>reply_to</c> tool.</summary>
public sealed record ReplyToResponse
{
    /// <summary>UTC timestamp at which the reply was accepted by the hub.</summary>
    [JsonPropertyName("replied_at")]
    [ReturnSchema(Description = "UTC reply timestamp (ISO-8601)")]
    public string RepliedAt { get; init; } = "";
}
