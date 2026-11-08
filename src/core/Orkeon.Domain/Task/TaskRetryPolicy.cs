namespace Orkeon.Domain.Task;

/// <summary>How the wait before a new attempt of a task grows.</summary>
public enum TaskRetryBackoff
{
    /// <summary>Every wait lasts <c>backoffMs</c>.</summary>
    Fixed,

    /// <summary>The wait doubles from one attempt to the next, starting at <c>backoffMs</c>.</summary>
    Exponential,
}

/// <summary>
/// The retry policy of a task (YAML <c>retry: {attempts, backoffMs, backoff}</c>): how many times the
/// task may run in all, the first run included, and how long to wait between two runs. A new attempt
/// runs the whole task again, tool calls included: nothing deduplicates a call that already succeeded.
/// </summary>
public sealed record TaskRetryPolicy
{
    /// <summary>The longest wait an exponential backoff reaches.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

    /// <summary>Initializes a new instance of <see cref="TaskRetryPolicy"/>.</summary>
    /// <param name="attempts">How many times the task may run, the first run included; at least 1.</param>
    /// <param name="backoffMs">The base wait before a new attempt, in milliseconds; not negative.</param>
    /// <param name="backoff">How the wait grows from one attempt to the next.</param>
    /// <exception cref="ArgumentOutOfRangeException">An argument is out of its range.</exception>
    public TaskRetryPolicy(int attempts, int backoffMs = 0, TaskRetryBackoff backoff = TaskRetryBackoff.Fixed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1, nameof(attempts));
        ArgumentOutOfRangeException.ThrowIfNegative(backoffMs, nameof(backoffMs));
        if (!Enum.IsDefined(backoff))
            throw new ArgumentOutOfRangeException(nameof(backoff), backoff, "backoff is fixed or exponential.");
        Attempts = attempts;
        BackoffMs = backoffMs;
        Backoff = backoff;
    }

    /// <summary>Gets how many times the task may run, the first run included.</summary>
    public int Attempts { get; }

    /// <summary>Gets how many times the task may run again after its first run: <see cref="Attempts"/> minus one.</summary>
    public int Retries => Attempts - 1;

    /// <summary>Gets the base wait before a new attempt, in milliseconds.</summary>
    public int BackoffMs { get; }

    /// <summary>Gets how the wait grows from one attempt to the next.</summary>
    public TaskRetryBackoff Backoff { get; }

    /// <summary>
    /// Gets the wait before attempt <paramref name="attempt"/> (2 is the first retry): zero before the
    /// first attempt, <see cref="BackoffMs"/> before each retry for a fixed backoff, doubled at each
    /// retry for an exponential one and never more than <see cref="MaxDelay"/>.
    /// </summary>
    /// <param name="attempt">The number of the attempt about to run, from 1.</param>
    /// <returns>The wait.</returns>
    public TimeSpan DelayBefore(int attempt)
    {
        if (attempt <= 1 || BackoffMs == 0)
            return TimeSpan.Zero;

        var factor = Backoff == TaskRetryBackoff.Exponential ? Math.Pow(2, Math.Min(attempt - 2, 30)) : 1;
        return TimeSpan.FromMilliseconds(Math.Min(BackoffMs * factor, MaxDelay.TotalMilliseconds));
    }
}
