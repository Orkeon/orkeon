using Orkeon.Domain.Constants.Task;

namespace Orkeon.Domain.Task;

/// <summary>
/// Value Object encapsulating execution time business rules for task management.
/// Defines thresholds for review requests, de-prioritization, and blocked task scheduling.
/// </summary>
public sealed record ExecutionTimePolicy
{
    /// <summary>
    /// Default execution time policy: review after 1 hour, de-prioritize after 2 hours,
    /// schedule blocked tasks after 10 minutes.
    /// </summary>
    public static readonly ExecutionTimePolicy Default = new();

    /// <summary>
    /// Gets the maximum execution time before a task should be flagged for review.
    /// </summary>
    public TimeSpan MaxExecutionTime { get; }

    /// <summary>
    /// Gets the execution time threshold after which a task should be de-prioritized.
    /// </summary>
    public TimeSpan DeadlineForDeprioritization { get; }

    /// <summary>
    /// Gets the execution time threshold after which a blocked task should be scheduled
    /// rather than immediately unblocked.
    /// </summary>
    public TimeSpan BlockedTaskScheduleThreshold { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="ExecutionTimePolicy"/>.
    /// </summary>
    /// <param name="maxExecutionTime">Time before review is requested (default: 1 hour).</param>
    /// <param name="deadlineForDeprioritization">Time before de-prioritization (default: 2 hours).</param>
    /// <param name="blockedTaskScheduleThreshold">Time before a blocked task is scheduled (default: 10 minutes).</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when any duration is negative or zero.
    /// </exception>
    public ExecutionTimePolicy(
        TimeSpan? maxExecutionTime = null,
        TimeSpan? deadlineForDeprioritization = null,
        TimeSpan? blockedTaskScheduleThreshold = null)
    {
        MaxExecutionTime = maxExecutionTime ?? TaskDefaults.DefaultMaxExecutionTime;
        DeadlineForDeprioritization = deadlineForDeprioritization ?? TaskDefaults.DefaultDeadlineForDeprioritization;
        BlockedTaskScheduleThreshold = blockedTaskScheduleThreshold ?? TaskDefaults.DefaultBlockedTaskScheduleThreshold;

        if (MaxExecutionTime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxExecutionTime), "Max execution time must be positive.");
        if (DeadlineForDeprioritization <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(deadlineForDeprioritization), "Deadline for de-prioritization must be positive.");
        if (BlockedTaskScheduleThreshold <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(blockedTaskScheduleThreshold), "Blocked task schedule threshold must be positive.");
    }

    /// <summary>
    /// Determines whether the task execution has exceeded the maximum execution time
    /// and should be reviewed.
    /// </summary>
    /// <param name="elapsed">The elapsed execution time.</param>
    /// <returns><see langword="true"/> if the task is overdue for review; otherwise <see langword="false"/>.</returns>
    public bool IsOverdue(TimeSpan elapsed) => elapsed > MaxExecutionTime;

    /// <summary>
    /// Determines whether the task should be de-prioritized based on execution time.
    /// </summary>
    /// <param name="elapsed">The elapsed execution time.</param>
    /// <returns><see langword="true"/> if the task should be de-prioritized; otherwise <see langword="false"/>.</returns>
    public bool ShouldDeprioritize(TimeSpan elapsed) => elapsed > DeadlineForDeprioritization;

    /// <summary>
    /// Determines whether a blocked task should be scheduled rather than immediately unblocked.
    /// </summary>
    /// <param name="elapsed">The elapsed execution time of the blocked task.</param>
    /// <returns><see langword="true"/> if the task should be scheduled; otherwise <see langword="false"/>.</returns>
    public bool ShouldScheduleBlockedTask(TimeSpan elapsed) => elapsed >= BlockedTaskScheduleThreshold;
}
