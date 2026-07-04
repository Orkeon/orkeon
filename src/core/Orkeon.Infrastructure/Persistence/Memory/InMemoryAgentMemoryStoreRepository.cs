using System.Collections.Concurrent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Persistence.Memory;

/// <summary>In-memory implementation of <see cref="IAgentMemoryStoreRepository"/> for testing and development.</summary>
public sealed class InMemoryAgentMemoryStoreRepository : IAgentMemoryStoreRepository
{
    private readonly ConcurrentDictionary<MemoryStoreId, AgentMemoryStore> _store = new();
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryAgentMemoryStoreRepository"/>.
    /// </summary>
    /// <param name="unitOfWork">The unit of work used to track aggregates for domain event dispatch.</param>
    public InMemoryAgentMemoryStoreRepository(IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<AgentMemoryStore?> GetByIdAsync(MemoryStoreId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.TryGetValue(id, out var store) ? store : null);

    /// <inheritdoc />
    public System.Threading.Tasks.Task AddAsync(AgentMemoryStore aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _store.TryAdd(aggregate.Id, aggregate);
        _unitOfWork.Track(aggregate);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task UpdateAsync(AgentMemoryStore aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        _store[aggregate.Id] = aggregate;
        _unitOfWork.Track(aggregate);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task DeleteAsync(MemoryStoreId id, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(id, out _);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<bool> ExistsAsync(MemoryStoreId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.ContainsKey(id));

    /// <inheritdoc />
    public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.Count);

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<AgentMemoryStore>> FindAsync(ISpecification<AgentMemoryStore> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentMemoryStore>>(_store.Values.Where(specification.IsSatisfiedBy).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<AgentMemoryStore>> FindAsync(ISpecification<AgentMemoryStore> specification, int skip, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentMemoryStore>>(_store.Values.Where(specification.IsSatisfiedBy).Skip(skip).Take(take).ToList());
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<int> CountAsync(ISpecification<AgentMemoryStore> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult(_store.Values.Count(specification.IsSatisfiedBy));
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<AgentMemoryStore> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return System.Threading.Tasks.Task.FromResult(_store.Values.Any(specification.IsSatisfiedBy));
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<AgentMemoryStore?> GetByAgentIdAsync(AgentId agentId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_store.Values.FirstOrDefault(s => s.OwnerAgentId == agentId));
}
