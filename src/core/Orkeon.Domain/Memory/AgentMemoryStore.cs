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

    private AgentMemoryStore(MemoryStoreId id) : base(id)
    {
        _shortTermMemory = [];
        _longTermMemory = [];
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
}
