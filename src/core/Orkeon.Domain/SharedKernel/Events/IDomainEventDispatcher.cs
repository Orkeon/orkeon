namespace Orkeon.Domain.SharedKernel.Events;

/// <summary>
/// Interface for dispatching domain events.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Dispatches a domain event to all registered handlers.
    /// </summary>
    System.Threading.Tasks.Task DispatchAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches multiple domain events.
    /// </summary>
    System.Threading.Tasks.Task DispatchManyAsync(IEnumerable<DomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
