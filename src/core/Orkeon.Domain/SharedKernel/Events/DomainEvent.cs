using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.Events;

/// <summary>
/// Base class for all domain events.
/// </summary>
public abstract record DomainEvent
{
    /// <summary>
    /// Gets the strongly-typed unique identifier for this domain event.
    /// </summary>
    public DomainEventId Id { get; init; } = DomainEventId.Create();

    /// <summary>
    /// Gets the timestamp when this domain event occurred.
    /// </summary>
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the name of this domain event type.
    /// </summary>
    public string EventName => GetType().Name;

    /// <summary>
    /// Gets the version of this domain event for serialization compatibility.
    /// </summary>
    public virtual int Version => 1;
}
