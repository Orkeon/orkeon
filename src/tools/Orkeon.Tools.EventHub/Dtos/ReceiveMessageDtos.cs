using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;

namespace Orkeon.Tools.EventHub.Dtos;

/// <summary>Request for the <c>receive_message</c> tool.</summary>
public sealed record ReceiveMessageRequest
{
    /// <summary>Wait timeout in milliseconds. Mutually exclusive with <see cref="WaitForever"/>.</summary>
    [JsonPropertyName("timeout_ms")]
    [FieldSchema(Description = "Wait timeout in ms (mutually exclusive with wait_forever)", IsRequired = false)]
    public int? TimeoutMs { get; init; }

    /// <summary>When true, waits indefinitely. Mutually exclusive with <see cref="TimeoutMs"/>.</summary>
    [JsonPropertyName("wait_forever")]
    [FieldSchema(Description = "Wait indefinitely (mutually exclusive with timeout_ms)", IsRequired = false)]
    public bool WaitForever { get; init; }

    /// <summary>Optional explicit mailbox; defaults to the current agent's mailbox.</summary>
    [JsonPropertyName("mailbox")]
    [FieldSchema(Description = "Optional mailbox URI. Defaults to the calling agent's mailbox.", IsRequired = false)]
    public string? Mailbox { get; init; }
}

/// <summary>Envelope summary returned by message-reading tools.</summary>
public sealed record EventMessageEnvelope
{
    /// <summary>Unique id of the message.</summary>
    [JsonPropertyName("message_id")]
    [ReturnSchema(Description = "Unique id of the message")]
    public string MessageId { get; init; } = "";

    /// <summary>Topic the message was published on.</summary>
    [JsonPropertyName("topic")]
    [ReturnSchema(Description = "Topic of the message")]
    public string Topic { get; init; } = "";

    /// <summary>Source crew id (always populated by the hub).</summary>
    [JsonPropertyName("source_crew_id")]
    [ReturnSchema(Description = "Source crew id")]
    public string SourceCrewId { get; init; } = "";

    /// <summary>Optional source agent id.</summary>
    [JsonPropertyName("source_agent_id")]
    [ReturnSchema(Description = "Source agent id (may be null)")]
    public string? SourceAgentId { get; init; }

    /// <summary>Optional correlation id (set on Send/Reply messages).</summary>
    [JsonPropertyName("correlation_id")]
    [ReturnSchema(Description = "Correlation id (may be null)")]
    public string? CorrelationId { get; init; }

    /// <summary>UTC publish timestamp (ISO-8601).</summary>
    [JsonPropertyName("published_at")]
    [ReturnSchema(Description = "UTC publish timestamp (ISO-8601)")]
    public string PublishedAt { get; init; } = "";

    /// <summary>Envelope metadata.</summary>
    [JsonPropertyName("metadata")]
    [ReturnSchema(Description = "Envelope metadata")]
    public ImmutableDictionary<string, string> Metadata { get; init; } = ImmutableDictionary<string, string>.Empty;

    /// <summary>JSON payload of the message.</summary>
    [JsonPropertyName("payload")]
    [ReturnSchema(Description = "JSON payload of the message")]
    public JsonNode? Payload { get; init; }
}

/// <summary>Response for the <c>receive_message</c> tool.</summary>
public sealed record ReceiveMessageResponse
{
    /// <summary>The received message, or <see langword="null"/> if the wait timed out.</summary>
    [JsonPropertyName("message")]
    [ReturnSchema(Description = "Received message (null if timed_out)")]
    public EventMessageEnvelope? Message { get; init; }

    /// <summary>Whether the wait timed out without receiving a message.</summary>
    [JsonPropertyName("timed_out")]
    [ReturnSchema(Description = "True if the wait timed out")]
    public bool TimedOut { get; init; }
}
