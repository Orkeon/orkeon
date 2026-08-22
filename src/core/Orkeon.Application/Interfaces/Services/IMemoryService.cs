
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using DomainMemoryType = Orkeon.Domain.Memory.MemoryType;

namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// Unified memory service over the four classic memory types.
/// </summary>
public interface IMemoryService
{
    /// <summary>
    /// Gets the complete memory system with all 4 types.
    /// </summary>
    ICrewMemorySystem GetMemorySystem(CrewId crewId);

    /// <summary>
    /// Forgets everything held for <paramref name="crewId"/> and releases its resources.
    /// In a one-shot CLI this never matters — the process exits. In a daemon, where every
    /// message loads a fresh crew with a fresh id, an unreleased memory system is one leaked
    /// entry per conversation, forever.
    /// </summary>
    void ReleaseMemorySystem(CrewId crewId);

    /// <summary>
    /// Saves a memory item to appropriate memory types.
    /// </summary>
    System.Threading.Tasks.Task SaveMemoryAsync(
        CrewId crewId,
        MemoryItem item,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches memory using relevance scoring.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> SearchMemoryAsync(
        CrewId crewId,
        string query,
        int maxResults = 10,
        DomainMemoryType? typeFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears memory for a crew.
    /// </summary>
    System.Threading.Tasks.Task ClearMemoryAsync(
        CrewId crewId,
        DomainMemoryType? typeFilter = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Complete memory system with the 4 classic types.
/// </summary>
public interface ICrewMemorySystem
{
    /// <summary>
    /// Recent interactions using sliding window.
    /// </summary>
    IShortTermMemory ShortTerm { get; }

    /// <summary>
    /// Valuable insights preserved across executions.
    /// </summary>
    ILongTermMemory LongTerm { get; }

    /// <summary>
    /// Information about people, places, concepts.
    /// </summary>
    IEntityMemory Entities { get; }

    /// <summary>
    /// Combines all memory types for coherent responses.
    /// </summary>
    IContextualMemory Contextual { get; }
}

/// <summary>
/// Short-term memory interface.
/// </summary>
public interface IShortTermMemory
{
    /// <summary>Add Async(Memory Item).</summary>
    System.Threading.Tasks.Task AddAsync(MemoryItem item);
    /// <summary>Get Recent Async(int).</summary>
    System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> GetRecentAsync(int count = 10);
    /// <summary>Clear Async().</summary>
    System.Threading.Tasks.Task ClearAsync();
    /// <summary>Clear().</summary>
    void Clear();
}

/// <summary>
/// Long-term memory interface with persistence.
/// </summary>
public interface ILongTermMemory
{
    /// <summary>Add Async(Memory Item).</summary>
    System.Threading.Tasks.Task AddAsync(MemoryItem item);
    /// <summary>Search Async(string, int).</summary>
    System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> SearchAsync(string query, int maxResults = 10);
    /// <summary>Clear Async().</summary>
    System.Threading.Tasks.Task ClearAsync();
}

/// <summary>
/// Entity memory for tracking specific entities.
/// </summary>
public interface IEntityMemory
{
    /// <summary>Adds an entity with the specified name, type, and attributes.</summary>
    System.Threading.Tasks.Task AddEntityAsync(string entityName, EntityType type, Dictionary<string, string> attributes);
    /// <summary>Gets the entity with the specified name.</summary>
    System.Threading.Tasks.Task<MemoryEntity?> GetEntityAsync(string entityName);
    /// <summary>Gets all entities of the specified type.</summary>
    System.Threading.Tasks.Task<IReadOnlyList<MemoryEntity>> GetEntitiesByTypeAsync(EntityType type);
    /// <summary>Updates the attributes of the specified entity.</summary>
    System.Threading.Tasks.Task UpdateEntityAsync(string entityName, Dictionary<string, string> attributes);
}

/// <summary>
/// Contextual memory combining all types.
/// </summary>
public interface IContextualMemory
{
    /// <summary>Get Context Async(string, int).</summary>
    System.Threading.Tasks.Task<string> GetContextAsync(string query, int maxTokens = 1000);
    /// <summary>Get Relevant Memories Async(string, int).</summary>
    System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> GetRelevantMemoriesAsync(string context, int count = 20);
}

