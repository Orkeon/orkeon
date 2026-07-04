using Orkeon.Domain.Common;
using Orkeon.Domain.Agent.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Domain.Agent;

/// <summary>
/// Repository interface for Agent aggregate.
/// Phase 3.1.3: Extends generic repository with agent-specific operations.
/// </summary>
public interface IAgentRepository : ISpecificationRepository<DomainAgent, AgentId>
{
    /// <summary>
    /// Gets multiple agents by their identifiers.
    /// </summary>
    Task<IReadOnlyList<DomainAgent>> GetByIdsAsync(
        IEnumerable<AgentId> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets agents by role.
    /// </summary>
    Task<IReadOnlyList<DomainAgent>> GetByRoleAsync(
        AgentRole role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets agents by status.
    /// </summary>
    Task<IReadOnlyList<DomainAgent>> GetByStatusAsync(
        AgentStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all available agents (not deactivated).
    /// </summary>
    Task<IReadOnlyList<DomainAgent>> GetAvailableAgentsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets agents with specific tools.
    /// </summary>
    Task<IReadOnlyList<DomainAgent>> GetAgentsWithToolsAsync(
        IEnumerable<ToolId> toolIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets agents by crew membership.
    /// </summary>
    Task<IReadOnlyList<DomainAgent>> GetByCrewIdAsync(
        CrewId crewId,
        CancellationToken cancellationToken = default);
}
