using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Orkeon.Application.Task.DTOs;

/// <summary>
/// Task output DTO for the Task module.
/// Enriched with fields from the Common/DTOs version (TaskId, AgentId, Success, ExecutionTime).
/// Content is an alias for RawOutput for backward compatibility.
/// </summary>
public sealed record TaskOutputDto
{
    /// <summary>Task identifier.</summary>
    [JsonPropertyName("task_id")]
    public string TaskId { get; init; } = string.Empty;

    /// <summary>Agent identifier that executed the task.</summary>
    [JsonPropertyName("agent_id")]
    public string AgentId { get; init; } = string.Empty;

    /// <summary>Raw output text.</summary>
    [JsonPropertyName("raw_output")]
    public string RawOutput { get; init; } = string.Empty;

    /// <summary>Task output content (alias for RawOutput).</summary>
    [JsonIgnore]
    public string Content => RawOutput;

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

    /// <summary>Metadata.</summary>
    [JsonPropertyName("metadata")]
    public ImmutableDictionary<string, object> Metadata { get; init; } = ImmutableDictionary<string, object>.Empty;
}
