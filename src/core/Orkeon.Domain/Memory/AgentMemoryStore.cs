using Orkeon.Domain.Common;
using Orkeon.Domain.Memory.Events;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Domain.Memory;

/// <summary>
/// Aggregate root for managing agent memory storage.
/// </summary>
public sealed class AgentMemoryStore : AggregateRoot<MemoryStoreId>
{
    private readonly List<MemoryItem> _shortTermMemory;
    private readonly List<MemoryItem> _longTermMemory;
    private readonly Dictionary<string, EntityMemory> _entityMemory;
    private readonly List<EpisodicMemory> _episodicMemory;

    /// <summary>
    /// Gets the owner agent identifier.
    /// </summary>
    public AgentId OwnerAgentId { get; private set; }

    /// <summary>
    /// Gets the short-term memory capacity.
    /// </summary>
    public int ShortTermCapacity { get; private set; } = MemoryDefaults.ShortTermCapacity;

    /// <summary>
    /// Gets the long-term memory capacity.
    /// </summary>
    public int LongTermCapacity { get; private set; } = MemoryDefaults.LongTermCapacity;

    /// <summary>
    /// Gets the short-term memories.
    /// </summary>
    public IReadOnlyList<MemoryItem> ShortTermMemory => _shortTermMemory.AsReadOnly();

    /// <summary>
    /// Gets the long-term memories.
    /// </summary>
    public IReadOnlyList<MemoryItem> LongTermMemory => _longTermMemory.AsReadOnly();

    /// <summary>
    /// Gets the entity memories.
    /// </summary>
    public IReadOnlyDictionary<string, EntityMemory> EntityMemory => _entityMemory.AsReadOnly();

    /// <summary>
    /// Gets the episodic memories.
    /// </summary>
    public IReadOnlyList<EpisodicMemory> EpisodicMemory => _episodicMemory.AsReadOnly();

    private AgentMemoryStore(MemoryStoreId id) : base(id)
    {
        _shortTermMemory = [];
        _longTermMemory = [];
        _entityMemory = [];
        _episodicMemory = [];
        OwnerAgentId = null!;
    }

    /// <summary>
    /// Creates a new memory store for an agent.
    /// </summary>
    public static AgentMemoryStore Create(
        AgentId ownerAgentId,
        int shortTermCapacity = MemoryDefaults.ShortTermCapacity,
        int longTermCapacity = MemoryDefaults.LongTermCapacity)
    {
        ArgumentNullException.ThrowIfNull(ownerAgentId);

        if (shortTermCapacity <= 0)
            throw new ArgumentException("Short-term capacity must be positive", nameof(shortTermCapacity));

        if (longTermCapacity <= 0)
            throw new ArgumentException("Long-term capacity must be positive", nameof(longTermCapacity));

        var store = new AgentMemoryStore(MemoryStoreId.Create())
        {

            OwnerAgentId = ownerAgentId,
            ShortTermCapacity = shortTermCapacity,
            LongTermCapacity = longTermCapacity
        };

        store.RaiseDomainEvent(new MemoryStoreCreatedEvent
        {
            MemoryStoreId = store.Id,
            OwnerAgentId = ownerAgentId
        });

        return store;
    }

    /// <summary>
    /// Rehydrates an <see cref="AgentMemoryStore"/> from persistence without raising domain events.
    /// Use this factory when loading an existing store from a database or external store.
    /// </summary>
#pragma warning disable S107 // Rehydration factory; parameters map 1:1 to persisted columns
    internal static AgentMemoryStore Restore(
        MemoryStoreId id,
        AgentId ownerAgentId,
        int shortTermCapacity,
        int longTermCapacity,
        IEnumerable<MemoryItem>? shortTermMemory = null,
        IEnumerable<MemoryItem>? longTermMemory = null,
        IDictionary<string, EntityMemory>? entityMemory = null,
        IEnumerable<EpisodicMemory>? episodicMemory = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(ownerAgentId);

        var store = new AgentMemoryStore(id)
        {
            OwnerAgentId = ownerAgentId,
            ShortTermCapacity = shortTermCapacity,
            LongTermCapacity = longTermCapacity
        };

        if (shortTermMemory != null)
            store._shortTermMemory.AddRange(shortTermMemory);

        if (longTermMemory != null)
            store._longTermMemory.AddRange(longTermMemory);

        if (entityMemory != null)
        {
            foreach (var kvp in entityMemory)
                store._entityMemory[kvp.Key] = kvp.Value;
        }

        if (episodicMemory != null)
            store._episodicMemory.AddRange(episodicMemory);

        return store;
    }
#pragma warning restore S107

    /// <summary>
    /// Adds a memory item to short-term memory.
    /// </summary>
    public void AddShortTermMemory(MemoryItem memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        _shortTermMemory.Add(memory);

        // Maintain capacity by removing oldest memories
        while (_shortTermMemory.Count > ShortTermCapacity)
        {
            var oldest = _shortTermMemory.OrderBy(m => m.Timestamp).First();
            _shortTermMemory.Remove(oldest);

            // Consider promoting to long-term memory
            if (oldest.ShouldPromoteToLongTerm())
            {
                PromoteToLongTermMemory(oldest);
            }
        }

        RaiseDomainEvent(new MemoryAddedEvent
        {
            MemoryStoreId = Id,
            MemoryItemId = memory.Id,
            Content = memory.Content,
            Importance = memory.Importance,
            AgentId = OwnerAgentId
        });
    }

    /// <summary>
    /// Promotes a memory to long-term storage.
    /// </summary>
    public void PromoteToLongTermMemory(MemoryItem memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        _longTermMemory.Add(memory);

        // Maintain capacity by removing least important memories
        while (_longTermMemory.Count > LongTermCapacity)
        {
            var leastImportant = _longTermMemory
                .OrderBy(m => m.Importance)
                .ThenBy(m => m.AccessCount)
                .First();
            _longTermMemory.Remove(leastImportant);
        }

        RaiseDomainEvent(new MemoryPromotedEvent
        {
            MemoryStoreId = Id,
            MemoryItemId = memory.Id,
            Content = memory.Content,
            Importance = memory.Importance,
            AgentId = OwnerAgentId
        });
    }

    /// <summary>
    /// Retrieves relevant memories based on a query.
    /// </summary>
    public Task<IEnumerable<MemoryItem>> RetrieveRelevantMemoriesAsync(
        string query,
        IEmbeddingService embeddingService,
        int limit = MemoryDefaults.DefaultSearchLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        ArgumentNullException.ThrowIfNull(embeddingService);

        if (limit <= 0)
            throw new ArgumentException("Limit must be positive", nameof(limit));

        return RetrieveRelevantMemoriesCoreAsync(query, embeddingService, limit);

        async Task<IEnumerable<MemoryItem>> RetrieveRelevantMemoriesCoreAsync(
            string query, IEmbeddingService embeddingService, int limit)
        {
            var queryEmbedding = await embeddingService.GetEmbeddingAsync(query).ConfigureAwait(false);

            // Combine short-term and long-term memories
            var allMemories = _shortTermMemory
                .Concat(_longTermMemory)
                .Select(m => new
                {
                    Memory = m,
                    Score = CosineSimilarity(m.Embedding, queryEmbedding)
                })
                .OrderByDescending(x => x.Score)
                .Take(limit)
                .Select(x => x.Memory);

            // Update access counts
            foreach (var memory in allMemories)
            {
                memory.IncrementAccessCount();
            }

            return allMemories;
        }
    }

    /// <summary>
    /// Builds a memory context for a task.
    /// </summary>
    public MemoryContext BuildContext(Task.ICrewTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var recentMemories = _shortTermMemory.TakeLast(5);

        var taskRelatedMemories = _longTermMemory
            .Where(m => m.Tags.Contains($"task_type:{task.GetType().Name}"))
            .OrderByDescending(m => m.Importance)
            .Take(10);

        var relevantEntities = _entityMemory.Values
            .Where(e => task.Description.Value.Contains(e.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return MemoryContext.Build(
            recentMemories.ToList(),
            taskRelatedMemories.ToList(),
            relevantEntities);
    }

    /// <summary>
    /// Adds or updates an entity memory.
    /// </summary>
    public void UpdateEntityMemory(string entityName, EntityMemory entityMemory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);

        ArgumentNullException.ThrowIfNull(entityMemory);

        _entityMemory[entityName] = entityMemory;

        RaiseDomainEvent(new EntityMemoryUpdatedEvent
        {
            AgentId = OwnerAgentId,
            EntityKey = entityName,
            UpdatedValue = entityMemory
        });
    }

    /// <summary>
    /// Adds an episodic memory.
    /// </summary>
    public void AddEpisodicMemory(EpisodicMemory episode)
    {
        ArgumentNullException.ThrowIfNull(episode);

        _episodicMemory.Add(episode);

        // Keep only recent episodes (last 100)
        const int maxEpisodes = 100;
        if (_episodicMemory.Count > maxEpisodes)
        {
            _episodicMemory.RemoveRange(0, _episodicMemory.Count - maxEpisodes);
        }

        RaiseDomainEvent(new EpisodicMemoryAddedEvent
        {
            AgentId = OwnerAgentId,
            EpisodeId = episode.Id,
            EpisodeTitle = episode.Title
        });
    }

    /// <summary>
    /// Clears short-term memory.
    /// </summary>
    public void ClearShortTermMemory()
    {
        _shortTermMemory.Clear();

        RaiseDomainEvent(new MemoryClearedEvent
        {
            MemoryStoreId = Id,
            MemoryType = MemoryType.ShortTerm,
            AgentId = OwnerAgentId
        });
    }

    /// <summary>
    /// Updates memory capacities.
    /// </summary>
    public void UpdateCapacities(int? shortTermCapacity = null, int? longTermCapacity = null)
    {
        if (shortTermCapacity.HasValue)
        {
            if (shortTermCapacity.Value <= 0)
                throw new ArgumentException("Short-term capacity must be positive", nameof(shortTermCapacity));
            ShortTermCapacity = shortTermCapacity.Value;
        }

        if (longTermCapacity.HasValue)
        {
            if (longTermCapacity.Value <= 0)
                throw new ArgumentException("Long-term capacity must be positive", nameof(longTermCapacity));
            LongTermCapacity = longTermCapacity.Value;
        }
    }

    private static float CosineSimilarity(IReadOnlyList<float>? embedding1, float[]? embedding2)
    {
        if (embedding1 == null || embedding2 == null)
            return 0f;

        if (embedding1.Count != embedding2.Length)
            return 0f;

        float dotProduct = 0f;
        float magnitude1 = 0f;
        float magnitude2 = 0f;

        for (int i = 0; i < embedding1.Count; i++)
        {
            dotProduct += embedding1[i] * embedding2[i];
            magnitude1 += embedding1[i] * embedding1[i];
            magnitude2 += embedding2[i] * embedding2[i];
        }

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0f;

        return dotProduct / (float)(Math.Sqrt(magnitude1) * Math.Sqrt(magnitude2));
    }
}
