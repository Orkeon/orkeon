namespace Orkeon.Domain.SharedKernel.Events;

/// <summary>
/// Interface for handling domain events.
/// </summary>
/// <typeparam name="TEvent">The type of domain event to handle.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "Canonical DDD domain-event-handler abstraction; the 'EventHandler' suffix conveys intent and is implemented across multiple projects.")]
public interface IDomainEventHandler<in TEvent> where TEvent : DomainEvent
{
    /// <summary>
    /// Handles the domain event.
    /// </summary>
    System.Threading.Tasks.Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
