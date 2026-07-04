using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.Events;

namespace Orkeon.Domain.Memory.Events;

/// <summary>
/// Event raised when a memory store is created.
/// </summary>
public sealed record MemoryStoreCreatedEvent : DomainEvent
{
    /// <summary>Gets the memory store identifier.</summary>
    public required MemoryStoreId MemoryStoreId { get; init; }
    /// <summary>Gets the identifier of the agent that owns the memory store.</summary>
    public required AgentId OwnerAgentId { get; init; }
}

/// <summary>
/// Event raised when a memory is added to a store.
/// </summary>
public sealed record MemoryAddedEvent : DomainEvent
{
    /// <summary>Gets the memory store identifier.</summary>
    public required MemoryStoreId MemoryStoreId { get; init; }
    /// <summary>Gets the memory item identifier.</summary>
    public required MemoryItemId MemoryItemId { get; init; }
    /// <summary>Gets the content of the memory that was added.</summary>
    public required string Content { get; init; }
    /// <summary>Gets the importance score of the memory.</summary>
    public required float Importance { get; init; }
    /// <summary>Gets the agent identifier that added the memory.</summary>
    public required AgentId AgentId { get; init; }
}

/// <summary>
/// Event raised when a memory is promoted from short-term to long-term.
/// </summary>
public sealed record MemoryPromotedEvent : DomainEvent
{
    /// <summary>Gets the memory store identifier.</summary>
    public required MemoryStoreId MemoryStoreId { get; init; }
    /// <summary>Gets the memory item identifier.</summary>
    public required MemoryItemId MemoryItemId { get; init; }
    /// <summary>Gets the content of the memory that was promoted.</summary>
    public required string Content { get; init; }
    /// <summary>Gets the importance score of the memory.</summary>
    public required float Importance { get; init; }
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
}

/// <summary>
/// Event raised when entity memory is updated.
/// </summary>
public sealed record EntityMemoryUpdatedEvent : DomainEvent
{
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the entity key that was updated.</summary>
    public required string EntityKey { get; init; }
    /// <summary>Gets the updated value.</summary>
    public object? UpdatedValue { get; init; }
}

/// <summary>
/// Event raised when an episodic memory is added.
/// </summary>
public sealed record EpisodicMemoryAddedEvent : DomainEvent
{
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
    /// <summary>Gets the episodic memory identifier.</summary>
    public required EpisodeId EpisodeId { get; init; }
    /// <summary>Gets the title of the episode.</summary>
    public required string EpisodeTitle { get; init; }
}

/// <summary>
/// Event raised when a memory store is cleared.
/// </summary>
public sealed record MemoryClearedEvent : DomainEvent
{
    /// <summary>Gets the memory store identifier.</summary>
    public required MemoryStoreId MemoryStoreId { get; init; }
    /// <summary>Gets the type of memory that was cleared.</summary>
    public required MemoryType MemoryType { get; init; }
    /// <summary>Gets the agent identifier.</summary>
    public required AgentId AgentId { get; init; }
}
