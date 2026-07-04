using Orkeon.Domain.Common;

namespace Orkeon.Domain.Memory;

/// <summary>
/// Repository interface for AgentMemoryStore aggregate.
/// </summary>
public interface IAgentMemoryStoreRepository : ISpecificationRepository<AgentMemoryStore, MemoryStoreId>
{
    /// <summary>Gets the memory store for a specific agent.</summary>
    Task<AgentMemoryStore?> GetByAgentIdAsync(AgentId agentId, CancellationToken cancellationToken = default);
}
