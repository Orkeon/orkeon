using System.Collections.Immutable;
using System.Text.Json.Serialization;

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
