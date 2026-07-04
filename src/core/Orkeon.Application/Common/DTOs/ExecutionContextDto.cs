using System.Collections.Immutable;
using System.Text.Json.Serialization;
using static Orkeon.Domain.Constants.Memory.MemoryDefaults;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Consolidated Task output DTO.
/// Merges fields from the former Common/DTOs (TaskId, AgentId, Content, Success, ExecutionTime, ToolsUsed)
/// and Task/DTOs (RawOutput, FormattedOutput, Format, SizeBytes, GeneratedAt, ValidationStatus, Metadata) versions.
/// </summary>
public sealed record TaskOutputDto
{
    /// <summary>Task identifier.</summary>
    [JsonPropertyName("task_id")]
    public string TaskId { get; init; } = string.Empty;

    /// <summary>Agent identifier that executed the task.</summary>
    [JsonPropertyName("agent_id")]
    public string AgentId { get; init; } = string.Empty;

    /// <summary>Task output content (alias for RawOutput, for backward compatibility).</summary>
    [JsonIgnore]
    public string Content { get => RawOutput; init => RawOutput = value; }

    /// <summary>Raw output text.</summary>
    [JsonPropertyName("raw_output")]
    public string RawOutput { get; init; } = string.Empty;

    /// <summary>Formatted output.</summary>
    [JsonPropertyName("formatted_output")]
    public string? FormattedOutput { get; init; }

    /// <summary>Structured output if available.</summary>
    [JsonPropertyName("structured_output")]
    public object? StructuredOutput { get; init; }

    /// <summary>Output format.</summary>
    [JsonPropertyName("format")]
    public string Format { get; init; } = "text";

    /// <summary>Whether the task was successful.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>Size in bytes.</summary>
    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; init; }

    /// <summary>Generation timestamp.</summary>
    [JsonPropertyName("generated_at")]
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Task completion timestamp.</summary>
    [JsonPropertyName("completed_at")]
    public DateTime CompletedAt { get; init; }

    /// <summary>Execution time.</summary>
    [JsonPropertyName("execution_time")]
    public TimeSpan ExecutionTime { get; init; }

    /// <summary>Validation status.</summary>
    [JsonPropertyName("validation_status")]
    public string ValidationStatus { get; init; } = "Valid";

    /// <summary>Tools used during execution.</summary>
    [JsonPropertyName("tools_used")]
    public IReadOnlyList<ToolUsageDto> ToolsUsed { get; init; } = [];

    /// <summary>Metadata.</summary>
    [JsonPropertyName("metadata")]
    public ImmutableDictionary<string, object> Metadata { get; init; } = ImmutableDictionary<string, object>.Empty;
}

/// <summary>
/// Data Transfer Object for execution context.
/// Contains all contextual information for task/crew execution.
/// </summary>
public sealed record ExecutionContextDto
{
    /// <summary>
    /// Unique identifier for the execution context.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Context variables and data.
    /// </summary>
    [JsonPropertyName("variables")]
    public Dictionary<string, object> Variables { get; init; } = [];

    /// <summary>
    /// Previous task outputs for reference.
    /// </summary>
    [JsonPropertyName("previous_outputs")]
    public IReadOnlyList<TaskOutputDto> PreviousOutputs { get; init; } = [];

    /// <summary>
    /// Memory-related context data.
    /// </summary>
    [JsonPropertyName("memory_context")]
    public MemoryContextDto? MemoryContext { get; init; }

    /// <summary>
    /// User-provided input data.
    /// </summary>
    [JsonPropertyName("user_input")]
    public string? UserInput { get; init; }

    /// <summary>
    /// Session identifier for tracking.
    /// </summary>
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    /// <summary>
    /// Execution timestamp.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Additional metadata for extensibility.
    /// Well-known keys "crew_id" and "variable_count" are also
    /// available as typed properties on this DTO.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Number of context variables (derived from Variables).
    /// Equivalent to Metadata["variable_count"] when populated by ExecutionMapper.
    /// </summary>
    public int VariableCount => Variables.Count;
}

/// <summary>
/// Data Transfer Object for memory context.
/// </summary>
public sealed record MemoryContextDto
{
    /// <summary>
    /// Short-term memory items.
    /// </summary>
    [JsonPropertyName("short_term_memory")]
    public IReadOnlyList<MemoryItemDto> ShortTermMemory { get; init; } = [];

    /// <summary>
    /// Long-term memory items.
    /// </summary>
    [JsonPropertyName("long_term_memory")]
    public IReadOnlyList<MemoryItemDto> LongTermMemory { get; init; } = [];

    /// <summary>
    /// Episodic memory items.
    /// </summary>
    [JsonPropertyName("episodic_memory")]
    public IReadOnlyList<MemoryItemDto> EpisodicMemory { get; init; } = [];

    /// <summary>
    /// Memory provider configuration.
    /// </summary>
    [JsonPropertyName("memory_provider")]
    public string MemoryProvider { get; init; } = DefaultProvider;
}

/// <summary>
/// Data Transfer Object for memory items.
/// </summary>
public sealed record MemoryItemDto
{
    /// <summary>
    /// Memory item identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Memory content.
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Memory item type (experience, fact, etc.).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Relevance score.
    /// </summary>
    [JsonPropertyName("relevance")]
    public double Relevance { get; init; }

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Associated agent ID.
    /// </summary>
    [JsonPropertyName("agent_id")]
    public string? AgentId { get; init; }

    /// <summary>
    /// Associated task ID.
    /// </summary>
    [JsonPropertyName("task_id")]
    public string? TaskId { get; init; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; init; } = [];
}

/// <summary>
/// Data Transfer Object for tool usage.
/// </summary>
public sealed record ToolUsageDto
{
    /// <summary>
    /// Tool identifier.
    /// </summary>
    [JsonPropertyName("tool_id")]
    public string ToolId { get; init; } = string.Empty;

    /// <summary>
    /// Tool name.
    /// </summary>
    [JsonPropertyName("tool_name")]
    public string ToolName { get; init; } = string.Empty;

    /// <summary>
    /// Tool input parameters.
    /// </summary>
    [JsonPropertyName("input")]
    public Dictionary<string, object> Input { get; init; } = [];

    /// <summary>
    /// Tool output.
    /// </summary>
    [JsonPropertyName("output")]
    public string Output { get; init; } = string.Empty;

    /// <summary>
    /// Whether the tool call was successful.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// Tool execution time.
    /// </summary>
    [JsonPropertyName("execution_time")]
    public TimeSpan ExecutionTime { get; init; }

    /// <summary>
    /// Error message if tool call failed.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>
    /// Tool call timestamp.
    /// </summary>
    [JsonPropertyName("called_at")]
    public DateTime CalledAt { get; init; } = DateTime.UtcNow;
}
