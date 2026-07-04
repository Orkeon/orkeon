using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Persistence;

/// <summary>
/// In-memory implementation of <see cref="IUnitOfWork"/> for testing and development.
/// Dispatches domain events from the tracked aggregate after persistence.
/// Restricted to one aggregate per scope (R37 — one command = one aggregate).
/// </summary>
/// <remarks>
/// This adapter intentionally has <b>no durable persist step</b>: aggregates live in the
/// in-memory repositories, so "saving" reduces to dispatching the pending domain events
/// (which is real and ordered). There is no crash recovery at this level by design — a
/// durable <see cref="IUnitOfWork"/> adapter would replace this one wholesale. Durable
/// persistence of <i>crew execution states</i> is a separate, opt-in mechanism: see
/// <c>ScopedCrewExecutionStateManager</c> + <c>IStateStore</c> (R3.8).
/// </remarks>
public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly List<IHasDomainEvents> _trackedAggregates = [];

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryUnitOfWork"/>.
    /// </summary>
    /// <param name="dispatcher">The domain event dispatcher used to publish events after save.</param>
    public InMemoryUnitOfWork(IDomainEventDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public void Track(IHasDomainEvents aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        // R37: One aggregate per unit-of-work scope.
        // Clear any previously tracked aggregate so that only the latest is retained.
        _trackedAggregates.Clear();
        _trackedAggregates.Add(aggregate);
    }

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 1. Persist step — intentionally empty for the in-memory adapter: aggregates
        //    already live in the in-memory repositories, so there is nothing to flush.
        //    A durable adapter (e.g., database-backed) implements this step for real
        //    and must dispatch events only after a successful persist.

        // 2. Collect all pending domain events from the tracked aggregate(s).
        var events = _trackedAggregates
            .SelectMany(a => a.DomainEvents)
            .ToList();

        // 3. Dispatch events AFTER the conceptual persist step.
        //    Wrapped in try/finally so ClearDomainEvents is always called,
        //    even if a handler throws — prevents stale events on retry.
        try
        {
            foreach (var domainEvent in events)
                await _dispatcher.DispatchAsync(domainEvent, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // 4. Clear events from the tracked aggregate(s) unconditionally.
            foreach (var aggregate in _trackedAggregates)
                aggregate.ClearDomainEvents();
        }

        return events.Count;
    }
}
