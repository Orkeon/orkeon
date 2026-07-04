using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Agent;

/// <summary>
/// Service interface for managing dynamic agent creation and lifecycle during crew execution.
/// </summary>
public interface IDynamicAgentService
{
    /// <summary>
    /// Spawns a new agent dynamically based on the provided request.
    /// </summary>
    /// <param name="request">The spawn request describing the agent to create.</param>
    /// <param name="registry">The registry to track the spawned agent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly spawned agent.</returns>
    Task<Domain.Agent.Agent> SpawnAgentAsync(
        AgentSpawnRequest request,
        DynamicAgentRegistry registry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates a dynamically created agent.
    /// </summary>
    /// <param name="agentId">The agent to terminate.</param>
    /// <param name="registry">The registry managing the agent.</param>
    /// <param name="reason">Optional termination reason.</param>
    /// <returns>True if the agent was terminated, false if not found.</returns>
    Task<bool> TerminateAgentAsync(
        AgentId agentId,
        DynamicAgentRegistry registry,
        string? reason = null);

    /// <summary>
    /// Validates whether a crew allows dynamic agents.
    /// </summary>
    /// <param name="crewId">The crew to validate.</param>
    /// <returns>True if the crew allows dynamic agent creation.</returns>
    Task<bool> AllowsDynamicAgentsAsync(CrewId crewId);

    /// <summary>
    /// Gets the maximum concurrent dynamic agents allowed for a crew.
    /// </summary>
    /// <param name="crewId">The crew to query.</param>
    /// <returns>The maximum number of concurrent dynamic agents, or null for unlimited.</returns>
    Task<int?> GetMaxConcurrentDynamicAgentsAsync(CrewId crewId);
}
