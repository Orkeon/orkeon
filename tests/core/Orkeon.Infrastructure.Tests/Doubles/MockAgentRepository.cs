using Orkeon.Domain.Common;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IAgentRepository with call tracking and in-memory storage.
/// </summary>
public class MockAgentRepository : IAgentRepository
{
    private readonly Dictionary<string, DomainAgent> _agents = [];

    // --- Tracking ---
    public int GetByIdCallCount { get; private set; }
    public AgentId? LastGetByIdArg { get; private set; }

    public int AddCallCount { get; private set; }
    public DomainAgent? LastAddedAgent { get; private set; }
    public int UpdateCallCount { get; private set; }
    public DomainAgent? LastUpdatedAgent { get; private set; }
    public int DeleteCallCount { get; private set; }
    public int ExistsCallCount { get; private set; }
    public int CountCallCount { get; private set; }

    public int GetByIdsCallCount { get; private set; }
    public int GetByRoleCallCount { get; private set; }
    public int GetByStatusCallCount { get; private set; }
    public int GetAvailableAgentsCallCount { get; private set; }
    public int GetAgentsWithToolsCallCount { get; private set; }
    public int GetByCrewIdCallCount { get; private set; }
    public int FindBySpecCallCount { get; private set; }
    public int FindBySpecPaginatedCallCount { get; private set; }
    public int CountBySpecCallCount { get; private set; }
    public int AnyBySpecCallCount { get; private set; }

    // --- Configuration ---
    public void AddAgentToStore(DomainAgent agent)
    {
        _agents[agent.Id] = agent;
    }

    private static string ToKey(AgentId id) => id.Value.ToString();

    // --- IRepository<DomainAgent, AgentId> ---
    public Task<DomainAgent?> GetByIdAsync(AgentId id, CancellationToken cancellationToken = default)
    {
        GetByIdCallCount++;
        LastGetByIdArg = id;
        _agents.TryGetValue(ToKey(id), out var agent);
        return Task.FromResult(agent);
    }

    public Task AddAsync(DomainAgent aggregate, CancellationToken cancellationToken = default)
    {
        AddCallCount++;
        LastAddedAgent = aggregate;
        _agents[aggregate.Id] = aggregate;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(DomainAgent aggregate, CancellationToken cancellationToken = default)
    {
        UpdateCallCount++;
        LastUpdatedAgent = aggregate;
        _agents[aggregate.Id] = aggregate;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(AgentId id, CancellationToken cancellationToken = default)
    {
        DeleteCallCount++;
        _agents.Remove(ToKey(id));
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(AgentId id, CancellationToken cancellationToken = default)
    {
        ExistsCallCount++;
        return Task.FromResult(_agents.ContainsKey(ToKey(id)));
    }

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        CountCallCount++;
        return Task.FromResult(_agents.Count);
    }

    // --- ISpecificationRepository<DomainAgent, AgentId> ---
    public Task<IReadOnlyList<DomainAgent>> FindAsync(
        ISpecification<DomainAgent> specification,
        CancellationToken cancellationToken = default)
    {
        FindBySpecCallCount++;
        IReadOnlyList<DomainAgent> result = _agents.Values
            .Where(a => specification.IsSatisfiedBy(a))
            .ToList()
            .AsReadOnly();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<DomainAgent>> FindAsync(
        ISpecification<DomainAgent> specification,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        FindBySpecPaginatedCallCount++;
        IReadOnlyList<DomainAgent> result = _agents.Values
            .Where(a => specification.IsSatisfiedBy(a))
            .Skip(skip)
            .Take(take)
            .ToList()
            .AsReadOnly();
        return Task.FromResult(result);
    }

    public Task<int> CountAsync(
        ISpecification<DomainAgent> specification,
        CancellationToken cancellationToken = default)
    {
        CountBySpecCallCount++;
        return Task.FromResult(_agents.Values.Count(a => specification.IsSatisfiedBy(a)));
    }

    public Task<bool> AnyAsync(
        ISpecification<DomainAgent> specification,
        CancellationToken cancellationToken = default)
    {
        AnyBySpecCallCount++;
        return Task.FromResult(_agents.Values.Any(a => specification.IsSatisfiedBy(a)));
    }

    // --- IAgentRepository specific ---
    public Task<IReadOnlyList<DomainAgent>> GetByIdsAsync(
        IEnumerable<AgentId> ids,
        CancellationToken cancellationToken = default)
    {
        GetByIdsCallCount++;
        var keys = ids.Select(ToKey).ToHashSet();
        var result = _agents
            .Where(kv => keys.Contains(kv.Key))
            .Select(kv => kv.Value)
            .ToList();
        IReadOnlyList<DomainAgent> readOnly = result.AsReadOnly();
        return Task.FromResult(readOnly);
    }

    public Task<IReadOnlyList<DomainAgent>> GetByRoleAsync(
        AgentRole role,
        CancellationToken cancellationToken = default)
    {
        GetByRoleCallCount++;
        IReadOnlyList<DomainAgent> result = _agents.Values.ToList().AsReadOnly();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<DomainAgent>> GetByStatusAsync(
        AgentStatus status,
        CancellationToken cancellationToken = default)
    {
        GetByStatusCallCount++;
        IReadOnlyList<DomainAgent> result = _agents.Values.ToList().AsReadOnly();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<DomainAgent>> GetAvailableAgentsAsync(
        CancellationToken cancellationToken = default)
    {
        GetAvailableAgentsCallCount++;
        IReadOnlyList<DomainAgent> result = _agents.Values.ToList().AsReadOnly();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<DomainAgent>> GetAgentsWithToolsAsync(
        IEnumerable<ToolId> toolIds,
        CancellationToken cancellationToken = default)
    {
        GetAgentsWithToolsCallCount++;
        IReadOnlyList<DomainAgent> result = _agents.Values.ToList().AsReadOnly();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<DomainAgent>> GetByCrewIdAsync(
        CrewId crewId,
        CancellationToken cancellationToken = default)
    {
        GetByCrewIdCallCount++;
        IReadOnlyList<DomainAgent> result = _agents.Values.ToList().AsReadOnly();
        return Task.FromResult(result);
    }
}
