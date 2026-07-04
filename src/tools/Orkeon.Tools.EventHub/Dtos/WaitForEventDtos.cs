using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.EventHub.Dtos;

/// <summary>Request for the <c>wait_for_event</c> tool.</summary>
public sealed record WaitForEventRequest
{
    /// <summary>Topic to wait on.</summary>
    [JsonPropertyName("topic")]
    [FieldSchema(Description = "Topic to wait on", Example = "order.received")]
    public string Topic { get; init; } = "";

    /// <summary>Wait timeout in milliseconds. Mutually exclusive with <see cref="WaitForever"/>.</summary>
    [JsonPropertyName("timeout_ms")]
    [FieldSchema(Description = "Wait timeout in ms (mutually exclusive with wait_forever)", IsRequired = false)]
    public int? TimeoutMs { get; init; }

    /// <summary>When true, waits indefinitely. Mutually exclusive with <see cref="TimeoutMs"/>.</summary>
    [JsonPropertyName("wait_forever")]
    [FieldSchema(Description = "Wait indefinitely (mutually exclusive with timeout_ms)", IsRequired = false)]
    public bool WaitForever { get; init; }

    /// <summary>Optional metadata constraints — only messages whose metadata is a superset of these pairs match.</summary>
    [JsonPropertyName("metadata_match")]
    [FieldSchema(Description = "Optional metadata match constraints", IsRequired = false)]
    public ImmutableDictionary<string, string>? MetadataMatch { get; init; }
}

/// <summary>Response for the <c>wait_for_event</c> tool.</summary>
public sealed record WaitForEventResponse
{
    /// <summary>The received message, or <see langword="null"/> if the wait timed out.</summary>
    [JsonPropertyName("message")]
    [ReturnSchema(Description = "Received message (null if timed_out)")]
    public EventMessageEnvelope? Message { get; init; }

    /// <summary>Whether the wait timed out.</summary>
    [JsonPropertyName("timed_out")]
    [ReturnSchema(Description = "True if the wait timed out")]
    public bool TimedOut { get; init; }
}
