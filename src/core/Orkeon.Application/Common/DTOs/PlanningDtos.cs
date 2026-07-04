using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Orkeon.Application.Constants.Execution;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Execution history DTO for tracking crew execution records.
/// </summary>
public sealed record ExecutionHistoryDto
{
    /// <summary>Gets or sets the id.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Gets or sets the crew id.</summary>
    [JsonPropertyName("crew_id")]
    public required string CrewId { get; init; }

    /// <summary>Gets or sets the status.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Gets or sets the started at.</summary>
    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; init; }

    /// <summary>Gets or sets the completed at.</summary>
    [JsonPropertyName("completed_at")]
    public DateTime? CompletedAt { get; init; }

    /// <summary>Gets or sets the duration.</summary>
    [JsonPropertyName("duration")]
    public TimeSpan? Duration { get; init; }

    /// <summary>Gets or sets the tasks completed.</summary>
    [JsonPropertyName("tasks_completed")]
    public int TasksCompleted { get; init; }

    /// <summary>Gets or sets the tasks failed.</summary>
    [JsonPropertyName("tasks_failed")]
    public int TasksFailed { get; init; }

    /// <summary>Gets or sets the success rate.</summary>
    [JsonPropertyName("success_rate")]
    public double SuccessRate { get; init; }

    /// <summary>Gets or sets the performance metrics.</summary>
    [JsonPropertyName("performance_metrics")]
    public PerformanceMetricsDto? PerformanceMetrics { get; init; }

    /// <summary>Gets or sets the error summary.</summary>
    [JsonPropertyName("error_summary")]
    public string? ErrorSummary { get; init; }

    /// <summary>Metadata.</summary>
    [JsonPropertyName("metadata")]
    public ImmutableDictionary<string, object> Metadata { get; init; } = [];
}

/// <summary>
/// Execution plan DTO with structured planning information.
/// </summary>
public sealed record ExecutionPlanDto
{
    /// <summary>Gets or sets the id.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Gets or sets the name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Gets or sets the strategy.</summary>
    [JsonPropertyName("strategy")]
    public required string Strategy { get; init; }

    /// <summary>Gets or sets the steps.</summary>
    [JsonPropertyName("steps")]
    public ImmutableList<ExecutionStepDto> Steps { get; init; } = [];

    /// <summary>Gets or sets the estimated duration.</summary>
    [JsonPropertyName("estimated_duration")]
    public TimeSpan EstimatedDuration { get; init; }

    /// <summary>Gets or sets the actual duration.</summary>
    [JsonPropertyName("actual_duration")]
    public TimeSpan? ActualDuration { get; init; }

    /// <summary>Gets or sets the status.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = StatusDefaults.PlannedStatus;

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
    /// Number of steps in the plan.
    /// </summary>
    public int StepCount => Steps.Count;

    /// <summary>
    /// Progress percentage based on completed steps.
    /// </summary>
    public double ProgressPercentage => StepCount == 0 ? 0.0 :
        (double)Steps.Count(s => s.Status == StatusDefaults.CompletedStatus) / StepCount * 100.0;
}

/// <summary>
/// Execution step DTO with detailed step information.
/// </summary>
public sealed record ExecutionStepDto
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

    /// <summary>Gets or sets the task id.</summary>
    [JsonPropertyName("task_id")]
    public string? TaskId { get; init; }

    /// <summary>Gets or sets the assigned agent.</summary>
    [JsonPropertyName("assigned_agent")]
    public string? AssignedAgent { get; init; }

    /// <summary>Gets or sets the status.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = StatusDefaults.PendingStatus;

    /// <summary>Gets or sets the estimated duration.</summary>
    [JsonPropertyName("estimated_duration")]
    public TimeSpan EstimatedDuration { get; init; }

    /// <summary>Gets or sets the actual duration.</summary>
    [JsonPropertyName("actual_duration")]
    public TimeSpan? ActualDuration { get; init; }

    /// <summary>Gets or sets the dependencies.</summary>
    [JsonPropertyName("dependencies")]
    public ImmutableList<string> Dependencies { get; init; } = [];

    /// <summary>Parameters.</summary>
    [JsonPropertyName("parameters")]
    public ImmutableDictionary<string, object> Parameters { get; init; } = [];

    /// <summary>Gets or sets the output.</summary>
    [JsonPropertyName("output")]
    public string? Output { get; init; }

    /// <summary>Gets or sets the error.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>Gets or sets the started at.</summary>
    [JsonPropertyName("started_at")]
    public DateTime? StartedAt { get; init; }

    /// <summary>Gets or sets the completed at.</summary>
    [JsonPropertyName("completed_at")]
    public DateTime? CompletedAt { get; init; }
}
