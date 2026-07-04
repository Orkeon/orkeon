using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Constants.Execution;

namespace Orkeon.Application.Agent.DTOs;

/// <summary>
/// Agent information DTO with immutable data structure.
/// </summary>
public sealed record AgentDto
{
    /// <summary>Gets or sets the id.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Gets or sets the name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Gets or sets the role.</summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>Gets or sets the goal.</summary>
    [JsonPropertyName("goal")]
    public required string Goal { get; init; }

    /// <summary>Gets or sets the backstory.</summary>
    [JsonPropertyName("backstory")]
    public required string Backstory { get; init; }

    /// <summary>Gets or sets the type.</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Gets or sets the status.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether verbose.
    /// </summary>
    [JsonPropertyName("verbose")]
    public bool Verbose { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether allow delegation.
    /// </summary>
    [JsonPropertyName("allow_delegation")]
    public bool AllowDelegation { get; init; }

    /// <summary>Gets or sets the max execution time.</summary>
    [JsonPropertyName("max_execution_time")]
    public int MaxExecutionTime { get; init; } = ExecutionDefaults.DefaultMaxExecutionSeconds;

    /// <summary>Gets or sets the tools.</summary>
    [JsonPropertyName("tools")]
    public ImmutableList<string> Tools { get; init; } = [];

    /// <summary>Gets or sets the llm.</summary>
    [JsonPropertyName("llm")]
    public LlmDto? Llm { get; init; }

    /// <summary>Gets or sets the capabilities.</summary>
    [JsonPropertyName("capabilities")]
    public AgentCapabilitiesDto? Capabilities { get; init; }

    /// <summary>Gets or sets the performance metrics.</summary>
    [JsonPropertyName("performance_metrics")]
    public PerformanceMetricsDto? PerformanceMetrics { get; init; }

    /// <summary>Gets or sets the created at.</summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Gets or sets the updated at.</summary>
    [JsonPropertyName("updated_at")]
    public DateTime? UpdatedAt { get; init; }

    /// <summary>Metadata.</summary>
    [JsonPropertyName("metadata")]
    public ImmutableDictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Creates a DTO with updated status.
    /// </summary>
    public AgentDto WithStatus(string status) => this with { Status = status, UpdatedAt = DateTime.UtcNow };

    /// <summary>
    /// Creates a DTO with updated performance metrics.
    /// </summary>
    public AgentDto WithPerformanceMetrics(PerformanceMetricsDto metrics) =>
        this with { PerformanceMetrics = metrics, UpdatedAt = DateTime.UtcNow };
}
