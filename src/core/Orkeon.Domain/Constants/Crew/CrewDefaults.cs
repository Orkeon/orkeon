namespace Orkeon.Domain.Constants.Crew;

/// <summary>
/// Default values for crew configuration parameters.
/// Centralizes magic numbers and strings used across the Crew aggregate and related services.
/// </summary>
public static class CrewDefaults
{
    /// <summary>
    /// Default maximum requests per minute for rate-limiting crew LLM calls.
    /// </summary>
    public const int DefaultMaxRpm = 100;

    /// <summary>
    /// Default maximum number of tasks allowed in a crew.
    /// </summary>
    public const int DefaultMaxTasks = 100;

    /// <summary>
    /// Default maximum number of agents allowed in a crew.
    /// </summary>
    public const int DefaultMaxAgents = 10;

    /// <summary>
    /// Default language code for crew output and operations.
    /// </summary>
    public const string DefaultLanguage = "en";

    /// <summary>
    /// Default process type for crew execution.
    /// </summary>
    public const string DefaultProcessType = "Sequential";

    /// <summary>Maximum wall-clock time for a full crew execution (30 min).</summary>
    public static readonly TimeSpan DefaultExecutionTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Default estimated duration for a single task (15 min).</summary>
    public static readonly TimeSpan DefaultEstimatedTaskDuration = TimeSpan.FromMinutes(15);

    // ── Validation limits ───────────────────────────────────

    /// <summary>Maximum length of <see cref="Orkeon.Domain.Crew.ValueObjects.CrewGoal"/> (2¹¹).</summary>
    public const int CrewGoalMaxLength = 2_048;
}
