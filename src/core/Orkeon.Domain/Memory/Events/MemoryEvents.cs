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
