using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Orkeon.Application.Constants.Messaging;
using Orkeon.Application.Constants.Execution;
using Orkeon.Domain.Constants.Http;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Collaboration context DTO for agent interactions.
/// </summary>
public sealed record CollaborationContextDto
{
    /// <summary>Gets or sets the id.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Gets or sets the type.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Gets or sets the participants.</summary>
    [JsonPropertyName("participants")]
    public ImmutableList<string> Participants { get; init; } = [];

    /// <summary>Gets or sets the leader.</summary>
    [JsonPropertyName("leader")]
    public string? Leader { get; init; }

    /// <summary>Gets or sets the status.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = StatusDefaults.Active;

    /// <summary>Gets or sets the created at.</summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Gets or sets the max duration.</summary>
    [JsonPropertyName("max_duration")]
    public TimeSpan? MaxDuration { get; init; }

    /// <summary>Shared State.</summary>
    [JsonPropertyName("shared_state")]
    public ImmutableDictionary<string, object> SharedState { get; init; } = [];

    /// <summary>Gets or sets the communication protocol.</summary>
    [JsonPropertyName("communication_protocol")]
    public CommunicationProtocolDto? CommunicationProtocol { get; init; }

    /// <summary>
    /// Number of participants in the collaboration.
    /// </summary>
    public int ParticipantCount => Participants.Count;

    /// <summary>
    /// Whether the collaboration has expired.
    /// </summary>
    public bool IsExpired => MaxDuration.HasValue && DateTime.UtcNow - CreatedAt > MaxDuration.Value;
}

/// <summary>
/// Communication protocol DTO for defining interaction patterns.
/// </summary>
public sealed record CommunicationProtocolDto
{
    /// <summary>Gets or sets the type.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Gets or sets the format.</summary>
    [JsonPropertyName("format")]
    public string Format { get; init; } = MessageDefaults.DefaultFormat;

    /// <summary>Gets or sets the timeout.</summary>
    [JsonPropertyName("timeout")]
    public TimeSpan Timeout { get; init; } = HttpDefaults.DefaultHttpTimeout;

    /// <summary>Gets or sets the max retries.</summary>
    [JsonPropertyName("max_retries")]
    public int MaxRetries { get; init; } = AgentDefaults.MaxRetryLimit;

    /// <summary>Parameters.</summary>
    [JsonPropertyName("parameters")]
    public ImmutableDictionary<string, object> Parameters { get; init; } = [];

    /// <summary>
    /// Whether the protocol is asynchronous.
    /// </summary>
    public bool IsAsync => Type is "MessageQueue" or "EventStream";
}
