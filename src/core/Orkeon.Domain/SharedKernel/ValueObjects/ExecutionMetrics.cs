using System.Globalization;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Execution metrics value object.
/// </summary>
public sealed record ExecutionMetrics : ValueObjectRecord
{
    /// <summary>Gets the total execution duration.</summary>
    public TimeSpan Duration { get; init; }
    /// <summary>Gets the number of successful executions.</summary>
    public int SuccessCount { get; init; }
    /// <summary>Gets the number of failed executions.</summary>
    public int FailureCount { get; init; }
    /// <summary>Gets the execution start time.</summary>
    public DateTime StartTime { get; init; }
    /// <summary>Gets the execution end time, or <see langword="null"/> if not yet completed.</summary>
    public DateTime? EndTime { get; init; }

    /// <summary>Initializes a new <see cref="ExecutionMetrics"/>.</summary>
    /// <param name="duration">The total execution duration (non-negative).</param>
    /// <param name="successCount">The number of successful executions (non-negative).</param>
    /// <param name="failureCount">The number of failed executions (non-negative).</param>
    /// <param name="startTime">The execution start time.</param>
    /// <param name="endTime">The optional execution end time.</param>
    private ExecutionMetrics(
        TimeSpan duration,
        int successCount,
        int failureCount,
        DateTime startTime,
        DateTime? endTime = null)
    {
        Duration = duration >= TimeSpan.Zero ? duration : throw new ArgumentException("Duration cannot be negative", nameof(duration));
        SuccessCount = successCount >= 0 ? successCount : throw new ArgumentException("Success count cannot be negative", nameof(successCount));
        FailureCount = failureCount >= 0 ? failureCount : throw new ArgumentException("Failure count cannot be negative", nameof(failureCount));
        StartTime = startTime;
        EndTime = endTime;
    }

    /// <summary>Creates a new <see cref="ExecutionMetrics"/>.</summary>
    /// <param name="duration">The total execution duration (non-negative).</param>
    /// <param name="successCount">The number of successful executions (non-negative).</param>
    /// <param name="failureCount">The number of failed executions (non-negative).</param>
    /// <param name="startTime">The execution start time.</param>
    /// <param name="endTime">The optional execution end time.</param>
    /// <returns>A new <see cref="ExecutionMetrics"/>.</returns>
    public static ExecutionMetrics Create(
        TimeSpan duration,
        int successCount,
        int failureCount,
        DateTime startTime,
        DateTime? endTime = null) =>
        new(duration, successCount, failureCount, startTime, endTime);

    /// <summary>Gets the total number of executions (success + failure).</summary>
    public int TotalCount => SuccessCount + FailureCount;
    /// <summary>Gets the success rate as a ratio (0.0–1.0).</summary>
    public double SuccessRate => TotalCount == 0 ? 0.0 : (double)SuccessCount / TotalCount;
    /// <summary>Gets the failure rate as a ratio (0.0–1.0).</summary>
    public double FailureRate => 1.0 - SuccessRate;
    /// <summary>Gets whether execution has completed.</summary>
    public bool IsCompleted => EndTime.HasValue;

    /// <summary>Creates an <see cref="ExecutionMetrics"/> representing a just-started execution.</summary>
    /// <param name="startTime">The start time.</param>
    /// <returns>A new <see cref="ExecutionMetrics"/> with zero counts.</returns>
    public static ExecutionMetrics Started(DateTime startTime) => Create(TimeSpan.Zero, 0, 0, startTime);

    /// <summary>Returns a new instance with the success count incremented by one.</summary>
    /// <returns>A new <see cref="ExecutionMetrics"/> with incremented success count.</returns>
    public ExecutionMetrics AddSuccess() => this with { SuccessCount = SuccessCount + 1 };
    /// <summary>Returns a new instance with the failure count incremented by one.</summary>
    /// <returns>A new <see cref="ExecutionMetrics"/> with incremented failure count.</returns>
    public ExecutionMetrics AddFailure() => this with { FailureCount = FailureCount + 1 };
    /// <summary>Returns a new instance marking the execution as complete at the given time.</summary>
    /// <param name="endTime">The completion time.</param>
    /// <returns>A new <see cref="ExecutionMetrics"/> with end time and duration set.</returns>
    public ExecutionMetrics Complete(DateTime endTime) => this with { EndTime = endTime, Duration = endTime - StartTime };

    /// <inheritdoc />
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"Duration: {Duration:hh\\:mm\\:ss}, Success: {SuccessCount}, Failures: {FailureCount}, Rate: {SuccessRate:P1}");
}
