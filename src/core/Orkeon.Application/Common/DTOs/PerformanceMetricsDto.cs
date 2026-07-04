using System.Text.Json.Serialization;
using Orkeon.Domain.Constants.Orchestration;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Performance metrics DTO with operational statistics.
/// </summary>
public sealed record PerformanceMetricsDto
{
    /// <summary>Gets or sets the throughput.</summary>
    [JsonPropertyName("throughput")]
    public double Throughput { get; init; } // Tasks per hour

    /// <summary>Gets or sets the average response time.</summary>
    [JsonPropertyName("average_response_time")]
    public double AverageResponseTime { get; init; } // Seconds

    /// <summary>Gets or sets the error rate.</summary>
    [JsonPropertyName("error_rate")]
    public double ErrorRate { get; init; } // Percentage

    /// <summary>Gets or sets the resource utilization.</summary>
    [JsonPropertyName("resource_utilization")]
    public double ResourceUtilization { get; init; } // Percentage

    /// <summary>Gets or sets the concurrent tasks.</summary>
    [JsonPropertyName("concurrent_tasks")]
    public int ConcurrentTasks { get; init; }

    /// <summary>Gets or sets the success count.</summary>
    [JsonPropertyName("success_count")]
    public int SuccessCount { get; init; }

    /// <summary>Gets or sets the failure count.</summary>
    [JsonPropertyName("failure_count")]
    public int FailureCount { get; init; }

    /// <summary>Gets or sets the measured at.</summary>
    [JsonPropertyName("measured_at")]
    public DateTime MeasuredAt { get; init; } = DateTime.UtcNow;

    /// <summary>Gets or sets the measurement period.</summary>
    [JsonPropertyName("measurement_period")]
    public TimeSpan MeasurementPeriod { get; init; } = OrchestrationDefaults.DefaultMeasurementPeriod;

    /// <summary>
    /// Calculated success rate.
    /// </summary>
    public double SuccessRate => (SuccessCount + FailureCount) == 0 ? 0.0 : (double)SuccessCount / (SuccessCount + FailureCount);

    /// <summary>
    /// Health status based on metrics.
    /// </summary>
    public string HealthStatus => (ErrorRate, AverageResponseTime, ResourceUtilization) switch
    {
        ( < 2.0, < 10.0, < 70.0) => "Excellent",
        ( < 5.0, < 30.0, < 85.0) => "Good",
        ( < 10.0, < 60.0, < 95.0) => "Fair",
        _ => "Poor"
    };
}
