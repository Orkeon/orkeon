using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.ValueObjects;

namespace Orkeon.Domain.Crew;

/// <summary>
/// Manages agent membership within a Crew: add, remove, and query operations.
/// Extracted from Crew to follow Single Responsibility Principle.
/// This is a domain helper (POCO), not a service — no dependency injection.
/// </summary>
internal sealed class CrewMemberManager
{
    private readonly List<AgentId> _agents;

    public CrewMemberManager(List<AgentId> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        _agents = agents;
    }

    /// <summary>
    /// Gets the agents as a read-only list.
    /// </summary>
    public IReadOnlyList<AgentId> Agents => _agents.AsReadOnly();

    /// <summary>
    /// Adds an agent to the crew. Validates that the agent is not already present
    /// and that the crew is not currently executing.
    /// </summary>
    public void AddAgent(AgentId agentId, CrewStatus status)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        if (_agents.Contains(agentId))
            throw new InvalidOperationException($"Agent {agentId} is already in this crew.");

        if (status == CrewStatus.Executing)
            throw new InvalidOperationException("Cannot add agents while crew is executing.");

        _agents.Add(agentId);
    }

    /// <summary>
    /// Removes an agent from the crew. Validates that the agent exists
    /// and that the crew is not currently executing.
    /// </summary>
    public void RemoveAgent(AgentId agentId, string reason, CrewStatus status)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!_agents.Contains(agentId))
            throw new InvalidOperationException($"Agent {agentId} is not in this crew.");

        if (status == CrewStatus.Executing)
            throw new InvalidOperationException("Cannot remove agents while crew is executing.");

        _agents.Remove(agentId);
    }

    /// <summary>
    /// Gets the first agent in the list, or null if empty.
    /// </summary>
    public AgentId? FirstOrDefault() => _agents.FirstOrDefault();

    /// <summary>
    /// Checks if an agent is in the crew.
    /// </summary>
    public bool Contains(AgentId agentId) => _agents.Contains(agentId);

    /// <summary>
    /// Checks if the crew has any agents.
    /// </summary>
    public bool Any() => _agents.Count > 0;
}
