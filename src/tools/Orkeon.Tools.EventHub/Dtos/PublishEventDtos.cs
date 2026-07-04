using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.EventHub.Dtos;

/// <summary>Request for the <c>publish_event</c> tool.</summary>
public sealed record PublishEventRequest
{
    /// <summary>Topic the event is published on. Must not start with the reserved prefix <c>_system.</c>.</summary>
    [JsonPropertyName("topic")]
    [FieldSchema(Description = "Topic name (must not start with _system.)", Example = "order.received")]
    public string Topic { get; init; } = "";

    /// <summary>JSON payload carried by the event.</summary>
    [JsonPropertyName("payload")]
    [FieldSchema(Description = "JSON payload to publish", Type = "any", IsRequired = false)]
    public JsonNode? Payload { get; init; }

    /// <summary>Restricts delivery to subscribers of this crew. Omit for a global broadcast.</summary>
    [JsonPropertyName("target_crew_id")]
    [FieldSchema(Description = "Crew scope. Omit for global broadcast.", IsRequired = false)]
    public string? TargetCrewId { get; init; }

    /// <summary>Optional metadata copied verbatim into the envelope.</summary>
    [JsonPropertyName("metadata")]
    [FieldSchema(Description = "Optional metadata key/value pairs", IsRequired = false)]
    public ImmutableDictionary<string, string>? Metadata { get; init; }

    /// <summary>When true, the payload is retained in the LastValueCache under <see cref="LastValueKey"/>.</summary>
    [JsonPropertyName("retain_as_last_value")]
    [FieldSchema(Description = "Retain the message in the LastValueCache (requires last_value_key)", IsRequired = false)]
    public bool RetainAsLastValue { get; init; }

    /// <summary>Cache key for <see cref="RetainAsLastValue"/>.</summary>
    [JsonPropertyName("last_value_key")]
    [FieldSchema(Description = "Cache key when retain_as_last_value is true", IsRequired = false)]
    public string? LastValueKey { get; init; }
}

/// <summary>Response for the <c>publish_event</c> tool.</summary>
public sealed record PublishEventResponse
{
    /// <summary>The unique id assigned to the published event.</summary>
    [JsonPropertyName("event_id")]
    [ReturnSchema(Description = "Unique id of the published event")]
    public string EventId { get; init; } = "";

    /// <summary>UTC timestamp at which the event was accepted by the hub.</summary>
    [JsonPropertyName("published_at")]
    [ReturnSchema(Description = "UTC publish timestamp (ISO-8601)")]
    public string PublishedAt { get; init; } = "";
}
