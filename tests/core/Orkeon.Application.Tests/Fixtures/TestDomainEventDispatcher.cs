using Orkeon.Domain.SharedKernel.Events;
namespace Orkeon.Application.Tests.Fixtures;

/// <summary>
/// Test double for IDomainEventDispatcher used in unit tests.
/// </summary>
public class TestDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly List<DomainEvent> _dispatchedEvents = [];

    public IReadOnlyList<DomainEvent> DispatchedEvents => _dispatchedEvents.AsReadOnly();
    public int DispatchCallCount { get; private set; }

    public System.Threading.Tasks.Task DispatchAsync(DomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        DispatchCallCount++;
        _dispatchedEvents.Add(domainEvent);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public void Clear()
    {
        _dispatchedEvents.Clear();
        DispatchCallCount = 0;
    }

    public bool HasDispatchedEvent<T>() where T : DomainEvent
    {
        return _dispatchedEvents.Any(e => e is T);
    }

    public T? GetDispatchedEvent<T>() where T : DomainEvent
    {
        return _dispatchedEvents.OfType<T>().FirstOrDefault();
    }

    public System.Threading.Tasks.Task DispatchManyAsync(IEnumerable<DomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            DispatchCallCount++;
            _dispatchedEvents.Add(domainEvent);
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
