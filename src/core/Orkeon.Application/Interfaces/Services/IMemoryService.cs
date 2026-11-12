
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
    /// <param name="item">The item to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task AddAsync(MemoryItem item, CancellationToken cancellationToken = default);
    /// <summary>Get Recent Async(int).</summary>
    /// <param name="count">How many recent items to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> GetRecentAsync(int count = 10, CancellationToken cancellationToken = default);
    /// <summary>Clear Async().</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task ClearAsync(CancellationToken cancellationToken = default);
    /// <summary>Clear().</summary>
    void Clear();
}

/// <summary>
/// Long-term memory interface with persistence.
/// </summary>
public interface ILongTermMemory
{
    /// <summary>Add Async(Memory Item).</summary>
    /// <param name="item">The item to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task AddAsync(MemoryItem item, CancellationToken cancellationToken = default);
    /// <summary>Search Async(string, int).</summary>
    /// <param name="query">The text to match.</param>
    /// <param name="maxResults">How many items to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> SearchAsync(string query, int maxResults = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// The entries of this memory closest to <paramref name="queryEmbedding"/>, best first: at most
    /// <paramref name="maxResults"/>, each scoring at least <paramref name="minScore"/> (cosine
    /// similarity). A memory backed by a provider searches it with its scope filter, applied by the
    /// provider before its limit; the in-process memory computes the cosine itself. Entries without
    /// a vector, or with a vector of another dimension, are never returned (GAP-30).
    /// </summary>
    /// <param name="queryEmbedding">The vector to compare the entries with.</param>
    /// <param name="maxResults">How many entries to return at most.</param>
    /// <param name="minScore">The least similarity an entry must reach.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<IReadOnlyList<ScoredMemoryItem>> SearchSimilarAsync(
        float[] queryEmbedding,
        int maxResults,
        float minScore,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the entry stored under <paramref name="key"/> — the <see cref="ScoredMemoryItem.Key"/>
    /// a search returned, else the item's id. Returns whether an entry was removed.
    /// </summary>
    /// <param name="key">The entry's storage key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Clear Async().</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Entity memory for tracking specific entities.
/// </summary>
public interface IEntityMemory
{
    /// <summary>Adds an entity with the specified name, type, and attributes.</summary>
    /// <param name="entityName">The entity's name.</param>
    /// <param name="type">The entity's type.</param>
    /// <param name="attributes">The entity's attributes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task AddEntityAsync(string entityName, EntityType type, Dictionary<string, string> attributes, CancellationToken cancellationToken = default);
    /// <summary>Gets the entity with the specified name.</summary>
    /// <param name="entityName">The entity's name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<MemoryEntity?> GetEntityAsync(string entityName, CancellationToken cancellationToken = default);
    /// <summary>Gets all entities of the specified type.</summary>
    /// <param name="type">The type to list.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<IReadOnlyList<MemoryEntity>> GetEntitiesByTypeAsync(EntityType type, CancellationToken cancellationToken = default);
    /// <summary>Updates the attributes of the specified entity.</summary>
    /// <param name="entityName">The entity's name.</param>
    /// <param name="attributes">The attributes to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task UpdateEntityAsync(string entityName, Dictionary<string, string> attributes, CancellationToken cancellationToken = default);
}

/// <summary>
/// Contextual memory combining all types.
/// </summary>
public interface IContextualMemory
{
    /// <summary>Get Context Async(string, int).</summary>
    /// <param name="query">The text the context answers.</param>
    /// <param name="maxTokens">The token budget of the context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<string> GetContextAsync(string query, int maxTokens = 1000, CancellationToken cancellationToken = default);
    /// <summary>Get Relevant Memories Async(string, int).</summary>
    /// <param name="context">The text to match.</param>
    /// <param name="count">How many items to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> GetRelevantMemoriesAsync(string context, int count = 20, CancellationToken cancellationToken = default);
}

