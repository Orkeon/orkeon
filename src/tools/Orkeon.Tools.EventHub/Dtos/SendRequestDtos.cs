using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.EventHub.Dtos;

/// <summary>Request for the <c>send_request</c> tool.</summary>
public sealed record SendRequestRequest
{
    /// <summary>Target mailbox URI.</summary>
    [JsonPropertyName("target_mailbox")]
    [FieldSchema(Description = "Mailbox URI of the responder", Example = "agent://crew-1/agent-7")]
    public string TargetMailbox { get; init; } = "";

    /// <summary>JSON payload carried by the request.</summary>
    [JsonPropertyName("payload")]
    [FieldSchema(Description = "JSON payload for the request", Type = "any", IsRequired = false)]
    public JsonNode? Payload { get; init; }

    /// <summary>Mandatory timeout in milliseconds. <c>Forever</c> is not allowed on Send (spec §9.3).</summary>
    [JsonPropertyName("timeout_ms")]
    [FieldSchema(Description = "Timeout in milliseconds. Must be strictly positive. Forever not allowed.", Example = 5000)]
    public int? TimeoutMs { get; init; }

    /// <summary>Optional metadata copied verbatim into the envelope.</summary>
    [JsonPropertyName("metadata")]
    [FieldSchema(Description = "Optional metadata key/value pairs", IsRequired = false)]
    public ImmutableDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Response for the <c>send_request</c> tool.</summary>
public sealed record SendRequestResponse
{
    /// <summary>Correlation identifier used to match a future <c>reply_to</c>.</summary>
    [JsonPropertyName("correlation_id")]
    [ReturnSchema(Description = "Correlation id that ties this request to its reply")]
    public string CorrelationId { get; init; } = "";

    /// <summary>JSON payload returned by the responder.</summary>
    [JsonPropertyName("response_payload")]
    [ReturnSchema(Description = "JSON payload returned by the responder")]
    public JsonNode? ResponsePayload { get; init; }
}
