namespace Orkeon.Application.Configuration;

/// <summary>
/// Feature flags for controlling the migration between different service implementations.
/// </summary>
public class FeatureFlags
{
    /// <summary>
    /// Enables the use of new domain models.
    /// </summary>
    public bool UseNewDomainModels { get; set; }

    /// <summary>
    /// Enables the use of AgentExecutionService.
    /// </summary>
    public bool UseAgentExecutionService { get; set; }

    /// <summary>
    /// Enables the use of SequentialCrewOrchestrator.
    /// </summary>
    public bool UseSequentialCrewOrchestrator { get; set; }

    /// <summary>
    /// Enables the use of CrewCompositionService.
    /// </summary>
    public bool UseCrewCompositionService { get; set; }

    /// <summary>
    /// Enables detailed logging for services.
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = true;

    /// <summary>
    /// Enables automatic fallback to previous services if current services fail.
    /// </summary>
    public bool EnableFallback { get; set; } = true;

    /// <summary>
    /// Percentage of requests to route to new services (0-100).
    /// Used for gradual rollout.
    /// </summary>
    public int TrafficPercentage { get; set; }

    /// <summary>
    /// List of specific agent IDs to use new services for.
    /// Empty list means all agents use the flag settings.
    /// </summary>
    public IReadOnlyList<string> AgentIds { get; init; } = [];

    /// <summary>
    /// List of specific crew IDs to use new services for.
    /// Empty list means all crews use the flag settings.
    /// </summary>
    public IReadOnlyList<string> CrewIds { get; init; } = [];

    /// <summary>
    /// Determines if new services should be used for a specific agent.
    /// </summary>
    public bool ShouldUseForAgent(string agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        if (AgentIds.Count > 0)
        {
            return AgentIds.Contains(agentId);
        }

        if (TrafficPercentage > 0 && TrafficPercentage < 100)
        {
            // Simple hash-based routing for consistent behavior
            var hash = agentId.GetHashCode(StringComparison.Ordinal);
            var bucket = Math.Abs(hash) % 100;
            return bucket < TrafficPercentage;
        }

        return UseNewDomainModels;
    }

    /// <summary>
    /// Determines if new services should be used for a specific crew.
    /// </summary>
    public bool ShouldUseForCrew(string crewId)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        if (CrewIds.Count > 0)
        {
            return CrewIds.Contains(crewId);
        }

        if (TrafficPercentage > 0 && TrafficPercentage < 100)
        {
            // Simple hash-based routing for consistent behavior
            var hash = crewId.GetHashCode(StringComparison.Ordinal);
            var bucket = Math.Abs(hash) % 100;
            return bucket < TrafficPercentage;
        }

        return UseNewDomainModels;
    }
}
