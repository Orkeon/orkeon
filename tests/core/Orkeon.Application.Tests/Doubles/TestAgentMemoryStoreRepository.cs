using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// In-memory test double for IAgentMemoryStoreRepository.
/// </summary>
public sealed class TestAgentMemoryStoreRepository : IAgentMemoryStoreRepository
{
    private readonly List<AgentMemoryStore> _stores = [];

    /// <summary>Gets the list of stored memory stores for assertion.</summary>
    public IReadOnlyList<AgentMemoryStore> StoredStores => _stores.AsReadOnly();

    /// <summary>Tracks whether AddAsync was called.</summary>
    public int AddAsyncCallCount { get; private set; }

    /// <summary>Tracks whether UpdateAsync was called.</summary>
    public int UpdateAsyncCallCount { get; private set; }

    public System.Threading.Tasks.Task AddAsync(AgentMemoryStore aggregate, CancellationToken cancellationToken = default)
    {
        AddAsyncCallCount++;
        _stores.Add(aggregate);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<AgentMemoryStore?> GetByIdAsync(MemoryStoreId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_stores.FirstOrDefault(s => s.Id == id));

    public System.Threading.Tasks.Task UpdateAsync(AgentMemoryStore aggregate, CancellationToken cancellationToken = default)
    {
        UpdateAsyncCallCount++;
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task DeleteAsync(MemoryStoreId id, CancellationToken cancellationToken = default)
    {
        _stores.RemoveAll(s => s.Id == id);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<bool> ExistsAsync(MemoryStoreId id, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_stores.Any(s => s.Id == id));

    public System.Threading.Tasks.Task<int> CountAsync(CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_stores.Count);

    public System.Threading.Tasks.Task<AgentMemoryStore?> GetByAgentIdAsync(AgentId agentId, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_stores.FirstOrDefault(s => s.OwnerAgentId == agentId));

    public System.Threading.Tasks.Task<IReadOnlyList<AgentMemoryStore>> FindAsync(ISpecification<AgentMemoryStore> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentMemoryStore>>(_stores.Where(specification.IsSatisfiedBy).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<IReadOnlyList<AgentMemoryStore>> FindAsync(ISpecification<AgentMemoryStore> specification, int skip, int take, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult<IReadOnlyList<AgentMemoryStore>>(_stores.Where(specification.IsSatisfiedBy).Skip(skip).Take(take).ToList().AsReadOnly());

    public System.Threading.Tasks.Task<int> CountAsync(ISpecification<AgentMemoryStore> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_stores.Count(specification.IsSatisfiedBy));

    public System.Threading.Tasks.Task<bool> AnyAsync(ISpecification<AgentMemoryStore> specification, CancellationToken cancellationToken = default)
        => System.Threading.Tasks.Task.FromResult(_stores.Any(specification.IsSatisfiedBy));

    /// <summary>Pre-seed a memory store for testing (simulates existing data).</summary>
    public void SeedStore(AgentMemoryStore store)
    {
        _stores.Add(store);
    }
}
