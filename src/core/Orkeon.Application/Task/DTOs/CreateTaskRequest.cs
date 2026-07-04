using System.Text.Json.Serialization;

namespace Orkeon.Application.Task.DTOs;

/// <summary>
/// Request DTO for creating a new task.
/// </summary>
public sealed record CreateTaskRequest
{
    /// <summary>
    /// Task description and instructions.
    /// </summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// Expected output format or description.
    /// </summary>
    [JsonPropertyName("expected_output")]
    public string? ExpectedOutput { get; init; }

    /// <summary>
    /// Role of the agent to assign to this task (optional).
    /// If not provided, system will select best agent.
    /// </summary>
    [JsonPropertyName("agent_role")]
    public string? AgentRole { get; init; }

    /// <summary>
    /// Specific agent ID to assign (optional).
    /// Takes precedence over AgentRole.
    /// </summary>
    [JsonPropertyName("agent_id")]
    public string? AgentId { get; init; }

    /// <summary>
    /// List of tool names to make available for this task.
    /// </summary>
    [JsonPropertyName("tools")]
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>
    /// Task context variables and data.
    /// </summary>
    [JsonPropertyName("context")]
    public Dictionary<string, object> Context { get; init; } = [];

    /// <summary>
    /// Task priority level.
    /// </summary>
    [JsonPropertyName("priority")]
    public TaskPriority Priority { get; init; } = TaskPriority.Normal;

    /// <summary>
    /// List of task IDs that this task depends on.
    /// </summary>
    [JsonPropertyName("dependencies")]
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    /// <summary>
    /// Output format (text, json, yaml, etc.).
    /// </summary>
    [JsonPropertyName("output_format")]
    public string OutputFormat { get; init; } = "text";

    /// <summary>
    /// Task execution settings.
    /// </summary>
    [JsonPropertyName("settings")]
    public TaskSettingsDto? Settings { get; init; }
}

/// <summary>
/// Request DTO for updating an existing task.
/// </summary>
public sealed record UpdateTaskRequest
{
    /// <summary>
    /// Updated task description (optional).
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Updated expected output (optional).
    /// </summary>
    [JsonPropertyName("expected_output")]
    public string? ExpectedOutput { get; init; }

    /// <summary>
    /// Updated agent assignment (optional).
    /// </summary>
    [JsonPropertyName("agent_id")]
    public string? AgentId { get; init; }

    /// <summary>
    /// Updated list of tool names (optional).
    /// </summary>
    [JsonPropertyName("tools")]
    public IReadOnlyList<string>? Tools { get; init; }

    /// <summary>
    /// Updated task context (optional).
    /// </summary>
    [JsonPropertyName("context")]
    public Dictionary<string, object>? Context { get; init; }

    /// <summary>
    /// Updated task priority (optional).
    /// </summary>
    [JsonPropertyName("priority")]
    public TaskPriority? Priority { get; init; }

    /// <summary>
    /// Updated dependencies (optional).
    /// </summary>
    [JsonPropertyName("dependencies")]
    public IReadOnlyList<string>? Dependencies { get; init; }

    /// <summary>
    /// Updated output format (optional).
    /// </summary>
    [JsonPropertyName("output_format")]
    public string? OutputFormat { get; init; }

    /// <summary>
    /// Updated task settings (optional).
    /// </summary>
    [JsonPropertyName("settings")]
    public TaskSettingsDto? Settings { get; init; }
}
