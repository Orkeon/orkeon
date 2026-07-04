using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Domain.Common;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Persistence.Agent;

/// <summary>
/// Lock-free in-memory implementation of <see cref="IAgentRegistrationStore"/> (R4.6 / ANT-001).
/// Registered as a <b>singleton</b> so agent registrations persist across A2A request scopes;
/// the scoped <see cref="SharedStoreAgentRepository"/> hydrates from this store on every call.
/// </summary>
public sealed class InMemoryAgentRegistrationStore : IAgentRegistrationStore
{
    private readonly ConcurrentDictionary<AgentId, DomainAgent> _agents = new();

    /// <inheritdoc />
    public int Count => _agents.Count;

    /// <inheritdoc />
    public DomainAgent? GetById(AgentId id)
        => _agents.TryGetValue(id, out var agent) ? agent : null;

    /// <inheritdoc />
    public bool Contains(AgentId id)
        => _agents.ContainsKey(id);

    /// <inheritdoc />
    public IReadOnlyList<DomainAgent> Snapshot()
        => _agents.Values.ToList();

    /// <inheritdoc />
    public bool TryAdd(DomainAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return _agents.TryAdd(agent.Id, agent);
    }

    /// <inheritdoc />
    public void Save(DomainAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agents[agent.Id] = agent;
    }

    /// <inheritdoc />
    public bool Remove(AgentId id)
        => _agents.TryRemove(id, out _);
}
