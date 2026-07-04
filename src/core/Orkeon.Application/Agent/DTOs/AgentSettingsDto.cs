using System.Text.Json.Serialization;
using static Orkeon.Domain.Constants.Memory.MemoryDefaults;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Application.Agent.DTOs;

/// <summary>
/// Agent settings DTO for configuration.
/// </summary>
public sealed record AgentSettingsDto
{
    /// <summary>
    /// Whether the agent operates in verbose mode.
    /// </summary>
    [JsonPropertyName("verbose")]
    public bool Verbose { get; init; }

    /// <summary>
    /// Whether the agent can delegate work to other agents.
    /// </summary>
    [JsonPropertyName("allow_delegation")]
    public bool AllowDelegation { get; init; } = true;

    /// <summary>
    /// Maximum number of iterations for task execution.
    /// </summary>
    [JsonPropertyName("max_iterations")]
    public int? MaxIterations { get; init; }

    /// <summary>
    /// Maximum requests per minute (rate limiting).
    /// </summary>
    [JsonPropertyName("max_rpm")]
    public double? MaxRPM { get; init; }

    /// <summary>
    /// Maximum execution time for a single task in seconds.
    /// </summary>
    [JsonPropertyName("max_execution_time_seconds")]
    public int? MaxExecutionTimeSeconds { get; init; }

    /// <summary>
    /// Memory settings for the agent.
    /// </summary>
    [JsonPropertyName("memory")]
    public AgentMemorySettingsDto? Memory { get; init; }
}

/// <summary>
/// Agent memory settings DTO.
/// </summary>
public sealed record AgentMemorySettingsDto
{
    /// <summary>
    /// Whether memory is enabled for this agent.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Maximum number of items in short-term memory.
    /// </summary>
    [JsonPropertyName("max_short_term_items")]
    public int MaxShortTermItems { get; init; } = DefaultMaxShortTermItems;

    /// <summary>
    /// Whether to persist memory to long-term storage.
    /// </summary>
    [JsonPropertyName("persist_long_term")]
    public bool PersistLongTerm { get; init; } = true;

    /// <summary>
    /// Memory relevance threshold (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("relevance_threshold")]
    public double RelevanceThreshold { get; init; } = SearchDefaults.DefaultSimilarityThreshold;
}
