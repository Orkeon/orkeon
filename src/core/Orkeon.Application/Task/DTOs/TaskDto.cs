using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.Constants.Crew;

namespace Orkeon.Application.Task.DTOs;

/// <summary>
/// Task information DTO with comprehensive task data.
/// </summary>
public sealed record TaskDto
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

    /// <summary>Gets or sets the expected output.</summary>
    [JsonPropertyName("expected_output")]
    public required string ExpectedOutput { get; init; }

    /// <summary>Gets or sets the assigned agent.</summary>
    [JsonPropertyName("assigned_agent")]
    public string? AssignedAgent { get; init; }

    /// <summary>Gets or sets the status.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Gets or sets the priority.</summary>
    [JsonPropertyName("priority")]
    public required string Priority { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether is async.
    /// </summary>
    [JsonPropertyName("is_async")]
    public bool IsAsync { get; init; }

    /// <summary>Gets or sets the estimated duration.</summary>
    [JsonPropertyName("estimated_duration")]
    public TimeSpan EstimatedDuration { get; init; } = CrewDefaults.DefaultEstimatedTaskDuration;

    /// <summary>Gets or sets the actual duration.</summary>
    [JsonPropertyName("actual_duration")]
    public TimeSpan? ActualDuration { get; init; }

    /// <summary>Gets or sets the dependencies.</summary>
    [JsonPropertyName("dependencies")]
    public ImmutableList<string> Dependencies { get; init; } = [];

    /// <summary>Gets or sets the required tools.</summary>
    [JsonPropertyName("required_tools")]
    public ImmutableList<string> RequiredTools { get; init; } = [];

    /// <summary>Gets or sets the output.</summary>
    [JsonPropertyName("output")]
    public TaskOutputDto? Output { get; init; }

    /// <summary>Gets or sets the complexity.</summary>
    [JsonPropertyName("complexity")]
    public TaskComplexityDto? Complexity { get; init; }

    /// <summary>Gets or sets the execution plan.</summary>
    [JsonPropertyName("execution_plan")]
    public ExecutionPlanDto? ExecutionPlan { get; init; }

    /// <summary>Gets or sets the created at.</summary>
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Gets or sets the started at.</summary>
    [JsonPropertyName("started_at")]
    public DateTime? StartedAt { get; init; }

    /// <summary>Gets or sets the completed at.</summary>
    [JsonPropertyName("completed_at")]
    public DateTime? CompletedAt { get; init; }

    /// <summary>Context.</summary>
    [JsonPropertyName("context")]
    public ImmutableDictionary<string, object> Context { get; init; } = [];

    /// <summary>
    /// Creates a DTO with updated status and timestamp.
    /// </summary>
    public TaskDto WithStatus(string status) => status switch
    {
        "InProgress" => this with { Status = status, StartedAt = DateTime.UtcNow },
        "Completed" or "Failed" => this with { Status = status, CompletedAt = DateTime.UtcNow },
        _ => this with { Status = status }
    };

    /// <summary>
    /// Creates a DTO with task output.
    /// </summary>
    public TaskDto WithOutput(TaskOutputDto output) =>
        this with { Output = output, CompletedAt = DateTime.UtcNow, Status = "Completed" };
}
