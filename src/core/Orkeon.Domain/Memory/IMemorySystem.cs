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
    System.Threading.Tasks.Task<AgentMemoryStore?> GetAgentMemoryStoreAsync(AgentId agentId);

    /// <summary>
    /// Creates a memory store for an agent.
    /// </summary>
    System.Threading.Tasks.Task<AgentMemoryStore> CreateAgentMemoryStoreAsync(AgentId agentId);

    /// <summary>
    /// Stores a memory item.
    /// </summary>
    System.Threading.Tasks.Task StoreMemoryAsync(AgentId agentId, MemoryItem memory);

    /// <summary>
    /// Retrieves relevant memories for a query.
    /// </summary>
    System.Threading.Tasks.Task<IEnumerable<MemoryItem>> RetrieveMemoriesAsync(AgentId agentId, string query, int limit = MemoryDefaults.DefaultSearchLimit);
}
