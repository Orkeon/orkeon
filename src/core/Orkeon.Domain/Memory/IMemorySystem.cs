using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Domain.Memory;

/// <summary>
/// Interface for the crew memory system.
/// </summary>
public interface IMemorySystem
{
    /// <summary>
    /// Gets the memory store for a specific agent.
    /// </summary>
    /// <param name="agentId">The agent the store belongs to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<AgentMemoryStore?> GetAgentMemoryStoreAsync(AgentId agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a memory store for an agent.
    /// </summary>
    /// <param name="agentId">The agent the store belongs to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<AgentMemoryStore> CreateAgentMemoryStoreAsync(AgentId agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a memory item.
    /// </summary>
    /// <param name="agentId">The agent the memory belongs to.</param>
    /// <param name="memory">The memory item to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task StoreMemoryAsync(AgentId agentId, MemoryItem memory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves relevant memories for a query.
    /// </summary>
    /// <param name="agentId">The agent whose memories are searched.</param>
    /// <param name="query">The text to match.</param>
    /// <param name="limit">How many memories to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<IEnumerable<MemoryItem>> RetrieveMemoriesAsync(AgentId agentId, string query, int limit = MemoryDefaults.DefaultSearchLimit, CancellationToken cancellationToken = default);
}
