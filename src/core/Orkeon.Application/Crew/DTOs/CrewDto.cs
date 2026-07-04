using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Orkeon.Application.Agent.DTOs;
using Orkeon.Application.Task.DTOs;
using Orkeon.Application.Memory.DTOs;
using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.Constants.Crew;

namespace Orkeon.Application.Crew.DTOs;

/// <summary>
/// Crew information DTO with team composition and configuration.
/// </summary>
public sealed record CrewDto
{
    /// <summary>Gets or sets the id.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Gets or sets the name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Gets or sets the description.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Gets or sets the process type.</summary>
    [JsonPropertyName("process_type")]
    public required string ProcessType { get; init; }

    /// <summary>Gets or sets the status.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Gets or sets the verbosity.</summary>
    [JsonPropertyName("verbosity")]
    public required string Verbosity { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether allow code execution.
    /// </summary>
    [JsonPropertyName("allow_code_execution")]
    public bool AllowCodeExecution { get; init; }

    /// <summary>Gets or sets the max agents.</summary>
    [JsonPropertyName("max_agents")]
    public int MaxAgents { get; init; } = 10;

    /// <summary>Gets or sets the max tasks.</summary>
    [JsonPropertyName("max_tasks")]
    public int MaxTasks { get; init; } = 100;

    /// <summary>Gets or sets the execution timeout.</summary>
    [JsonPropertyName("execution_timeout")]
    public TimeSpan ExecutionTimeout { get; init; } = CrewDefaults.DefaultExecutionTimeout;

    /// <summary>Gets or sets the agents.</summary>
    [JsonPropertyName("agents")]
    public ImmutableList<AgentDto> Agents { get; init; } = [];

    /// <summary>Gets or sets the tasks.</summary>
    [JsonPropertyName("tasks")]
    public ImmutableList<TaskDto> Tasks { get; init; } = [];

    /// <summary>Gets or sets the memory configuration.</summary>
    [JsonPropertyName("memory_config")]
    public MemoryConfigurationDto? MemoryConfiguration { get; init; }

    /// <summary>Gets or sets the performance metrics.</summary>
    [JsonPropertyName("performance_metrics")]
    public PerformanceMetricsDto? PerformanceMetrics { get; init; }

    /// <summary>Gets or sets the execution history.</summary>
    [JsonPropertyName("execution_history")]
    public ImmutableList<ExecutionHistoryDto> ExecutionHistory { get; init; } = [];

    /// <summary>Gets or sets the created at.</summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Gets or sets the started at.</summary>
    [JsonPropertyName("started_at")]
    public DateTime? StartedAt { get; init; }

    /// <summary>Gets or sets the completed at.</summary>
    [JsonPropertyName("completed_at")]
    public DateTime? CompletedAt { get; init; }

    /// <summary>Metadata.</summary>
    [JsonPropertyName("metadata")]
    public ImmutableDictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Agent count in the crew.
    /// </summary>
    public int AgentCount => Agents.Count;

    /// <summary>
    /// Task count in the crew.
    /// </summary>
    public int TaskCount => Tasks.Count;

    /// <summary>
    /// Creates a DTO with updated status.
    /// </summary>
    public CrewDto WithStatus(string status) => status switch
    {
        "Executing" => this with { Status = status, StartedAt = DateTime.UtcNow },
        "Completed" or "Failed" => this with { Status = status, CompletedAt = DateTime.UtcNow },
        _ => this with { Status = status }
    };
}
