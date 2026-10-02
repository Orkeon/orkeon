using Orkeon.Domain.SharedKernel.Events;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IDomainEventDispatcher"/> that records every event it is handed,
/// in order, and can be told to throw for one event type to stand in for a failing handler.
/// Thread-safe: a parallel wave's agents run while the strategy dispatches (GAP-21).
/// </summary>
public sealed class RecordingDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly List<DomainEvent> _dispatched = [];

    /// <summary>When set, dispatching an event of this type throws, after it is recorded.</summary>
    public Type? ThrowOn { get; set; }

    /// <summary>The events dispatched so far, in dispatch order (a snapshot).</summary>
    public IReadOnlyList<DomainEvent> Dispatched
    {
        get
        {
            lock (_dispatched)
                return [.. _dispatched];
        }
    }

    public System.Threading.Tasks.Task DispatchAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        lock (_dispatched)
            _dispatched.Add(domainEvent);
        if (ThrowOn is not null && ThrowOn.IsInstanceOfType(domainEvent))
            throw new InvalidOperationException($"handler for {domainEvent.GetType().Name} failed");
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public async System.Threading.Tasks.Task DispatchManyAsync(IEnumerable<DomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
            await DispatchAsync(domainEvent, cancellationToken).ConfigureAwait(false);
    }
}
