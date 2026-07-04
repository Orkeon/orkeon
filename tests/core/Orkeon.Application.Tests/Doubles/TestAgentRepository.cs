using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// In-memory test double for IAgentRepository.
/// </summary>
public sealed class TestAgentRepository : IAgentRepository
{
    private readonly List<DomainAgent> _agents = [];

    /// <summary>Gets the list of stored agents for assertion.</summary>
    public IReadOnlyList<DomainAgent> StoredAgents => _agents.AsReadOnly();

    /// <summary>Tracks whether AddAsync was called.</summary>
    public int AddAsyncCallCount { get; private set; }

    public System.Threading.Tasks.Task AddAsync(DomainAgent aggregate, CancellationToken cancellationToken = default)
    {
        AddAsyncCallCount++;
        _agents.Add(aggregate);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<DomainAgent?> GetByIdAsync(AgentId id, CancellationToken cancellationToken = default)
    {
        DomainAgent? result = _agents.FirstOrDefault(a => a.Id == id);
        return System.Threading.Tasks.Task.FromResult(result);
    }

    public System.Threading.Tasks.Task UpdateAsync(DomainAgent aggregate, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.CompletedTask;

    public System.Threading.Tasks.Task DeleteAsync(AgentId id, CancellationToken cancellationToken = default)
    {
        _agents.RemoveAll(a => a.Id == id);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<bool> ExistsAsync(AgentId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_agents.Any(a => a.Id == id));


    public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_agents.Count);

    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByIdsAsync(IEnumerable<AgentId> ids, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_agents.Where(a => ids.Contains(a.Id)).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByRoleAsync(AgentRole role, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_agents.Where(a => a.Role.Value == role.Value).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByStatusAsync(AgentStatus status, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_agents.Where(a => a.Status == status).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetAvailableAgentsAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_agents.Where(a => a.Status != AgentStatus.Deactivated).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetAgentsWithToolsAsync(IEnumerable<ToolId> toolIds, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(new List<DomainAgent>().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByCrewIdAsync(CrewId crewId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(new List<DomainAgent>().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> FindAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_agents.Where(a => specification.IsSatisfiedBy(a)).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> FindAsync(ISpecification<DomainAgent> specification, int skip, int take, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_agents.Where(a => specification.IsSatisfiedBy(a)).Skip(skip).Take(take).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<int> CountAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_agents.Count(a => specification.IsSatisfiedBy(a)));

    public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_agents.Any(a => specification.IsSatisfiedBy(a)));
}
