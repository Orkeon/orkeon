using System.Collections.Concurrent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Application.Interfaces.Ports;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Persistence.Agent;

/// <summary>In-memory implementation of <see cref="IAgentRepository"/> for testing and development.</summary>
public sealed class InMemoryAgentRepository : IAgentRepository
{
    private readonly ConcurrentDictionary<AgentId, DomainAgent> _store = new();
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryAgentRepository"/>.
    /// </summary>
    /// <param name="unitOfWork">The unit of work used to track aggregates for domain event dispatch.</param>
    public InMemoryAgentRepository(IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<DomainAgent?> GetByIdAsync(AgentId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.TryGetValue(id, out var agent) ? agent : null);

    /// <inheritdoc />
    public System.Threading.Tasks.Task AddAsync(DomainAgent aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _store.TryAdd(aggregate.Id, aggregate);
        _unitOfWork.Track(aggregate);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task UpdateAsync(DomainAgent aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _store[aggregate.Id] = aggregate;
        _unitOfWork.Track(aggregate);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task DeleteAsync(AgentId id, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(id, out _);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<bool> ExistsAsync(AgentId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.ContainsKey(id));

    /// <inheritdoc />
    public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.Count);

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> FindAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_store.Values.Where(specification.IsSatisfiedBy).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> FindAsync(ISpecification<DomainAgent> specification, int skip, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_store.Values.Where(specification.IsSatisfiedBy).Skip(skip).Take(take).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<int> CountAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult(_store.Values.Count(specification.IsSatisfiedBy));
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<DomainAgent> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult(_store.Values.Any(specification.IsSatisfiedBy));
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByIdsAsync(IEnumerable<AgentId> ids, CancellationToken cancellationToken = default)
    {
        var idSet = ids.ToHashSet();
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_store.Values.Where(a => idSet.Contains(a.Id)).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByRoleAsync(AgentRole role, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_store.Values.Where(a => a.Role == role).ToList());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByStatusAsync(AgentStatus status, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_store.Values.Where(a => a.Status == status).ToList());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetAvailableAgentsAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(_store.Values.Where(a => a.Status != AgentStatus.Deactivated).ToList());

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetAgentsWithToolsAsync(IEnumerable<ToolId> toolIds, CancellationToken cancellationToken = default)
    {
        // ToolId is Guid-based but ITool uses string Name - return agents that have any tools
        // as a reasonable approximation for in-memory implementation
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>(
            _store.Values.Where(a => a.Tools.Count > 0).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<DomainAgent>> GetByCrewIdAsync(CrewId crewId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<DomainAgent>>([]);
}
