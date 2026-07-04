using Orkeon.Domain.SharedKernel.Events;

namespace Orkeon.Domain.Common;

/// <summary>
/// Generic aggregate root with a strongly-typed identifier.
/// Inherits from <see cref="Entity{TEntityId}"/> and implements <see cref="IHasDomainEvents"/>.
/// </summary>
/// <typeparam name="TEntityId">The strongly-typed entity identifier type.</typeparam>
public abstract class AggregateRoot<TEntityId> : Entity<TEntityId>, IHasDomainEvents
    where TEntityId : EntityId<TEntityId>, new()
{
    private readonly List<DomainEvent> _domainEvents = [];


    /// <summary>
    /// Gets the uncommitted domain events.
    /// </summary>
    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// Initializes a new instance of the aggregate root with the specified identifier.
    /// </summary>
    /// <param name="id">The typed identifier.</param>
    protected AggregateRoot(TEntityId id) : base(id) { }

    /// <summary>
    /// Raises a domain event that will be dispatched after the aggregate is saved.
    /// </summary>
    /// <param name="domainEvent">The domain event to raise.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1030", Justification = "DDD aggregate pattern: queues a domain event for post-save dispatch, not a CLR event.")]
    protected void RaiseDomainEvent(DomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <summary>
    /// Clears all domain events. This is typically called after events have been dispatched.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Marks this aggregate as updated and increments the version.
    /// </summary>
    protected void MarkAsUpdated()
    {
        UpdatedAt = DateTime.UtcNow;
        Version++;
    }
}
