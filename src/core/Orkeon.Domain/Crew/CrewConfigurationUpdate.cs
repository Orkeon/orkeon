namespace Orkeon.Domain.Crew;

/// <summary>
/// Options for updating crew configuration. All properties are nullable to allow partial updates.
/// </summary>
public sealed class CrewConfigurationUpdate
{
    /// <summary>
    /// Whether to enable verbose logging.
    /// </summary>
    public bool? Verbose { get; init; }

    /// <summary>
    /// Whether to enable planning before execution.
    /// </summary>
    public bool? Planning { get; init; }

    /// <summary>
    /// Maximum requests per minute.
    /// </summary>
    public int? MaxRpm { get; init; }

    /// <summary>
    /// Whether to share the crew among agents.
    /// </summary>
    public bool? ShareCrew { get; init; }

    /// <summary>
    /// Output log file path.
    /// </summary>
    public string? OutputLogFile { get; init; }

    /// <summary>
    /// Language for the crew output.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Whether to return full output from all tasks.
    /// </summary>
    public bool? FullOutput { get; init; }

    /// <summary>
    /// Whether to enable memory for the crew.
    /// </summary>
    public bool? MemoryEnabled { get; init; }
}
