namespace Orkeon.Domain.Constants.Task;

/// <summary>
/// Default values for task execution policies, timeouts, and scheduling thresholds.
/// Centralises task-related magic values used across the Orkeon platform.
/// </summary>
public static class TaskDefaults
{
    /// <summary>Default timeout for general operations and planning (5 min).</summary>
    public static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Maximum execution time before a task is flagged for review (1 h).</summary>
    public static readonly TimeSpan DefaultMaxExecutionTime = TimeSpan.FromHours(1);

    /// <summary>Execution time after which a task should be deprioritised (2 h).</summary>
    public static readonly TimeSpan DefaultDeadlineForDeprioritization = TimeSpan.FromHours(2);

    /// <summary>Execution time after which a blocked task is scheduled (10 min).</summary>
    public static readonly TimeSpan DefaultBlockedTaskScheduleThreshold = TimeSpan.FromMinutes(10);

    // ── Validation limits ───────────────────────────────────

    /// <summary>Maximum length of <see cref="Orkeon.Domain.Task.ValueObjects.TaskDescription"/> (2¹⁵).</summary>
    public const int TaskDescriptionMaxLength = 32_768;

    /// <summary>Maximum length of <see cref="Orkeon.Domain.Task.ValueObjects.ExpectedOutput"/> (2¹²).</summary>
    public const int ExpectedOutputMaxLength = 4_096;
}
