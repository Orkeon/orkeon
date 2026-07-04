using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.Events;

namespace Orkeon.Infrastructure.DomainEvents;

/// <summary>
/// Internal helper for dispatching and clearing domain events.
/// WARNING: Should only be called from UnitOfWork implementations or test utilities.
/// Production code should use IUnitOfWork.SaveChangesAsync() to ensure events are dispatched after persistence.
/// </summary>
internal static class AggregateEventHelper
{
    /// <summary>
    /// Dispatches all pending domain events from the aggregate and then clears them.
    /// </summary>
    public static Task DispatchAndClearEventsAsync(
        IHasDomainEvents aggregate,
        IDomainEventDispatcher dispatcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(dispatcher);
        return DispatchAndClearEventsCoreAsync(aggregate, dispatcher, cancellationToken);
    }

    private static async Task DispatchAndClearEventsCoreAsync(
        IHasDomainEvents aggregate,
        IDomainEventDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var events = aggregate.DomainEvents;
        if (events.Count == 0) return;

        await dispatcher.DispatchManyAsync(events, cancellationToken).ConfigureAwait(false);
        aggregate.ClearDomainEvents();
    }
}
