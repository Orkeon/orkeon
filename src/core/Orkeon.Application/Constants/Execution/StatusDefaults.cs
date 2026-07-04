namespace Orkeon.Application.Constants.Execution;

/// <summary>
/// Default status strings and type labels used across agents, tasks, and crews.
/// Centralizes magic string literals used in DTOs and registries.
/// </summary>
public static class StatusDefaults
{
    /// <summary>
    /// Default active status string for agents and collaborations.
    /// </summary>
    public const string Active = "Active";

    /// <summary>
    /// Default agent type for newly created agents.
    /// </summary>
    public const string DefaultAgentType = "Worker";

    /// <summary>
    /// Default confidence level label for agent capabilities.
    /// </summary>
    public const string DefaultConfidenceLevel = "Medium";

    /// <summary>
    /// Default certification level for agent capabilities.
    /// </summary>
    public const string DefaultCertificationLevel = "Standard";

    /// <summary>
    /// Default verbosity level for crew and agent configuration.
    /// </summary>
    public const string DefaultVerbosityLevel = "Normal";

    /// <summary>
    /// Status for a planned (not yet started) execution step or plan.
    /// </summary>
    public const string PlannedStatus = "Planned";

    /// <summary>
    /// Status for a pending (queued) execution step.
    /// </summary>
    public const string PendingStatus = "Pending";

    /// <summary>
    /// Status for a completed execution step or plan.
    /// </summary>
    public const string CompletedStatus = "Completed";

    /// <summary>
    /// Default sort direction for list queries.
    /// </summary>
    public const string DefaultSortDirection = "asc";

    /// <summary>
    /// Default reason provided when terminating all agents at end of crew execution.
    /// </summary>
    public const string DefaultCompletionReason = "Crew execution completed";
}
