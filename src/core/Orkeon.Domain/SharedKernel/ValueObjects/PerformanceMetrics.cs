using System.Globalization;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Performance metrics value object for monitoring and optimization.
/// </summary>
public sealed record PerformanceMetrics : ValueObjectRecord
{
    /// <summary>Gets the throughput in tasks per hour.</summary>
    public double Throughput { get; init; }
    /// <summary>Gets the average response time in seconds.</summary>
    public double AverageResponseTime { get; init; }
    /// <summary>Gets the error rate as a percentage (0–100).</summary>
    public double ErrorRate { get; init; }
    /// <summary>Gets the resource utilization as a percentage (0–100).</summary>
    public double ResourceUtilization { get; init; }
    /// <summary>Gets the number of concurrent tasks.</summary>
    public int ConcurrentTasks { get; init; }
    /// <summary>Gets when these metrics were measured.</summary>
    public DateTime MeasuredAt { get; init; }

    // Health thresholds
    private const double HealthyErrorRateThreshold = 5.0;
    private const double HealthyResponseTimeThreshold = 30.0;
    private const double HealthyResourceUtilizationThreshold = 90.0;

    // Degraded / needs-attention thresholds
    private const double DegradedErrorRateThreshold = 10.0;
    private const double DegradedResponseTimeThreshold = 60.0;
    private const double DegradedResourceUtilizationThreshold = 95.0;

    // Excellent preset values
    private const double ExcellentThroughput = 100.0;
    private const double ExcellentAverageResponseTime = 1.0;
    private const double ExcellentErrorRate = 0.1;
    private const double ExcellentResourceUtilization = 50.0;
    private const int ExcellentConcurrentTasks = 10;

    // Poor preset values
    private const double PoorThroughput = 10.0;
    private const double PoorAverageResponseTime = 30.0;
    private const double PoorErrorRate = 15.0;
    private const double PoorResourceUtilization = 95.0;
    private const int PoorConcurrentTasks = 50;

    /// <summary>Initializes a new <see cref="PerformanceMetrics"/>.</summary>
    /// <param name="throughput">Tasks per hour (non-negative).</param>
    /// <param name="averageResponseTime">Average response time in seconds (non-negative).</param>
    /// <param name="errorRate">Error rate percentage (0–100).</param>
    /// <param name="resourceUtilization">Resource utilization percentage (0–100).</param>
    /// <param name="concurrentTasks">Number of concurrent tasks (non-negative).</param>
    private PerformanceMetrics(
        double throughput,
        double averageResponseTime,
        double errorRate,
        double resourceUtilization,
        int concurrentTasks)
    {
        Throughput = throughput >= 0 ? throughput : throw new ArgumentException("Throughput cannot be negative", nameof(throughput));
        AverageResponseTime = averageResponseTime >= 0 ? averageResponseTime : throw new ArgumentException("Response time cannot be negative", nameof(averageResponseTime));
        ErrorRate = errorRate is >= 0 and <= 100 ? errorRate : throw new ArgumentException("Error rate must be between 0 and 100", nameof(errorRate));
        ResourceUtilization = resourceUtilization is >= 0 and <= 100 ? resourceUtilization : throw new ArgumentException("Resource utilization must be between 0 and 100", nameof(resourceUtilization));
        ConcurrentTasks = concurrentTasks >= 0 ? concurrentTasks : throw new ArgumentException("Concurrent tasks cannot be negative", nameof(concurrentTasks));
        MeasuredAt = DateTime.UtcNow;
    }

    /// <summary>Creates a new <see cref="PerformanceMetrics"/>.</summary>
    /// <param name="throughput">Tasks per hour (non-negative).</param>
    /// <param name="averageResponseTime">Average response time in seconds (non-negative).</param>
    /// <param name="errorRate">Error rate percentage (0–100).</param>
    /// <param name="resourceUtilization">Resource utilization percentage (0–100).</param>
    /// <param name="concurrentTasks">Number of concurrent tasks (non-negative).</param>
    /// <returns>A new <see cref="PerformanceMetrics"/>.</returns>
    public static PerformanceMetrics Create(
        double throughput,
        double averageResponseTime,
        double errorRate,
        double resourceUtilization,
        int concurrentTasks) =>
        new(throughput, averageResponseTime, errorRate, resourceUtilization, concurrentTasks);

    /// <summary>Gets whether the system is operating in a healthy state.</summary>
    public bool IsHealthy => ErrorRate < HealthyErrorRateThreshold && AverageResponseTime < HealthyResponseTimeThreshold && ResourceUtilization < HealthyResourceUtilizationThreshold;
    /// <summary>Gets whether the system requires attention due to degraded performance.</summary>
    public bool NeedsAttention => ErrorRate > DegradedErrorRateThreshold || AverageResponseTime > DegradedResponseTimeThreshold || ResourceUtilization > DegradedResourceUtilizationThreshold;

    /// <summary>Gets an excellent performance metrics sample.</summary>
    public static PerformanceMetrics Excellent => Create(ExcellentThroughput, ExcellentAverageResponseTime, ExcellentErrorRate, ExcellentResourceUtilization, ExcellentConcurrentTasks);
    /// <summary>Gets a poor performance metrics sample.</summary>
    public static PerformanceMetrics Poor => Create(PoorThroughput, PoorAverageResponseTime, PoorErrorRate, PoorResourceUtilization, PoorConcurrentTasks);

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Throughput: {Throughput:F1}/h, Response: {AverageResponseTime:F1}s, Errors: {ErrorRate:F1}%, Util: {ResourceUtilization:F1}%");
}
