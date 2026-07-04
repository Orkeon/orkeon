using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Domain.Task;

/// <summary>
/// Value Object encapsulating retry business rules for task execution.
/// Defines the maximum number of retries and the threshold for priority escalation.
/// </summary>
public sealed record RetryPolicy
{
    /// <summary>
    /// Default retry policy: <see cref="AgentDefaults.MaxRetryLimit"/> max retries, priority increase after 2 retries.
    /// </summary>
    public static readonly RetryPolicy Default = new();

    /// <summary>
    /// Gets the maximum number of retries allowed before a task is considered permanently failed.
    /// </summary>
    public int MaxRetries { get; }

    /// <summary>
    /// Gets the retry count threshold at which a failed task's priority should be increased.
    /// </summary>
    public int PriorityEscalationThreshold { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="RetryPolicy"/>.
    /// </summary>
    /// <param name="maxRetries">Maximum number of retries allowed (default: <see cref="AgentDefaults.MaxRetryLimit"/>).</param>
    /// <param name="priorityEscalationThreshold">Retry count at which priority should be escalated (default: 2).</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="maxRetries"/> is negative
    /// or <paramref name="priorityEscalationThreshold"/> is negative.
    /// </exception>
    public RetryPolicy(int maxRetries = AgentDefaults.MaxRetryLimit, int priorityEscalationThreshold = 2)
    {
        if (maxRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "Max retries must not be negative.");
        if (priorityEscalationThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(priorityEscalationThreshold), "Priority escalation threshold must not be negative.");

        MaxRetries = maxRetries;
        PriorityEscalationThreshold = priorityEscalationThreshold;
    }

    /// <summary>
    /// Determines whether the task should retry given the current retry count.
    /// </summary>
    /// <param name="currentRetries">The current number of retries attempted.</param>
    /// <returns><see langword="true"/> if a retry is allowed; otherwise <see langword="false"/>.</returns>
    public bool ShouldRetry(int currentRetries) => currentRetries < MaxRetries;

    /// <summary>
    /// Determines whether the task's priority should be escalated given the current retry count.
    /// </summary>
    /// <param name="currentRetries">The current number of retries attempted.</param>
    /// <returns><see langword="true"/> if priority should be increased; otherwise <see langword="false"/>.</returns>
    public bool ShouldEscalatePriority(int currentRetries) => currentRetries >= PriorityEscalationThreshold;
}
