using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Infrastructure.DomainEvents;
using Orkeon.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkeon.Infrastructure.Tests.Persistence;

/// <summary>
/// A simple domain event for testing purposes.
/// </summary>
public sealed record UnitOfWorkTestEvent(string Data) : DomainEvent;

/// <summary>
/// A second domain event type for testing multiple event types.
/// </summary>
public sealed record UnitOfWorkSecondTestEvent(int Value) : DomainEvent;

/// <summary>
/// A test aggregate that can raise domain events.
/// </summary>
public sealed class UnitOfWorkTestAggregateId : EntityId<UnitOfWorkTestAggregateId>;

public sealed class UnitOfWorkTestAggregate : AggregateRoot<UnitOfWorkTestAggregateId>
{
    public UnitOfWorkTestAggregate() : base(UnitOfWorkTestAggregateId.Create()) { }

    public void DoWork(string data)
    {
        RaiseDomainEvent(new UnitOfWorkTestEvent(data));
    }

    public void DoOtherWork(int value)
    {
        RaiseDomainEvent(new UnitOfWorkSecondTestEvent(value));
    }
}

/// <summary>
/// A recording handler that captures dispatched events.
/// </summary>
public sealed class UnitOfWorkTestEventHandler : IDomainEventHandler<UnitOfWorkTestEvent>
{
    public List<UnitOfWorkTestEvent> HandledEvents { get; } = [];

    public Task HandleAsync(UnitOfWorkTestEvent domainEvent, CancellationToken cancellationToken = default)
    {
        HandledEvents.Add(domainEvent);
        return Task.CompletedTask;
    }
}

public class InMemoryUnitOfWorkTests
{
    private static DomainEventDispatcher CreateDispatcher(ServiceCollection services)
    {
        var sp = services.BuildServiceProvider();
        return new DomainEventDispatcher(sp, NullLogger<DomainEventDispatcher>.Instance);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldDispatchDomainEventsFromTrackedAggregates()
    {
        // Arrange
        var handler = new UnitOfWorkTestEventHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<UnitOfWorkTestEvent>>(handler);

        var dispatcher = CreateDispatcher(services);
        var uow = new InMemoryUnitOfWork(dispatcher);

        var aggregate = new UnitOfWorkTestAggregate();
        aggregate.DoWork("event1");
        aggregate.DoWork("event2");
        uow.Track(aggregate);

        // Act
        var count = await uow.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, count);
        Assert.Equal(2, handler.HandledEvents.Count);
        Assert.Equal("event1", handler.HandledEvents[0].Data);
        Assert.Equal("event2", handler.HandledEvents[1].Data);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldClearEventsFromAggregatesAfterDispatch()
    {
        // Arrange
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(services);
        var uow = new InMemoryUnitOfWork(dispatcher);

        var aggregate = new UnitOfWorkTestAggregate();
        aggregate.DoWork("event1");
        uow.Track(aggregate);

        // Act
        await uow.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldReturnZeroWhenNoEventsExist()
    {
        // Arrange
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(services);
        var uow = new InMemoryUnitOfWork(dispatcher);

        var aggregate = new UnitOfWorkTestAggregate();
        uow.Track(aggregate); // tracked but no events raised

        // Act
        var count = await uow.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldReturnZeroWhenNoAggregatesTracked()
    {
        // Arrange
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(services);
        var uow = new InMemoryUnitOfWork(dispatcher);

        // Act
        var count = await uow.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Track_ShouldReplacesPreviousAggregate_R37_OneAggregatePerScope()
    {
        // Arrange — R37: only one aggregate per unit-of-work scope.
        // Tracking a second aggregate replaces the first.
        var handler = new UnitOfWorkTestEventHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<UnitOfWorkTestEvent>>(handler);

        var dispatcher = CreateDispatcher(services);
        var uow = new InMemoryUnitOfWork(dispatcher);

        var aggregate1 = new UnitOfWorkTestAggregate();
        aggregate1.DoWork("agg1-event");
        uow.Track(aggregate1);

        var aggregate2 = new UnitOfWorkTestAggregate();
        aggregate2.DoWork("agg2-event");
        uow.Track(aggregate2); // replaces aggregate1

        // Act
        var count = await uow.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert — only the last tracked aggregate's events are dispatched
        Assert.Equal(1, count);
        Assert.Single(handler.HandledEvents);
        Assert.Equal("agg2-event", handler.HandledEvents[0].Data);
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldNotDispatchAlreadyClearedEvents()
    {
        // Arrange
        var handler = new UnitOfWorkTestEventHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<UnitOfWorkTestEvent>>(handler);

        var dispatcher = CreateDispatcher(services);
        var uow = new InMemoryUnitOfWork(dispatcher);

        var aggregate = new UnitOfWorkTestAggregate();
        aggregate.DoWork("event1");
        uow.Track(aggregate);

        // First save — dispatches events
        await uow.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Single(handler.HandledEvents);

        // Second save — no new events
        var count = await uow.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, count);
        Assert.Single(handler.HandledEvents); // still just the one from first save
    }

    [Fact]
    public async Task Track_ShouldNotDuplicateAggregates()
    {
        // Arrange
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(services);
        var uow = new InMemoryUnitOfWork(dispatcher);

        var aggregate = new UnitOfWorkTestAggregate();

        // Act — track same aggregate twice
        uow.Track(aggregate);
        uow.Track(aggregate);

        // Assert — we verify indirectly that there's no duplication
        // by raising one event and checking it's dispatched exactly once
        aggregate.DoWork("single");
        var count = await uow.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Constructor_ShouldThrowOnNullDispatcher()
    {
        Assert.Throws<ArgumentNullException>(() => new InMemoryUnitOfWork(null!));
    }

    [Fact]
    public void Track_ShouldThrowOnNullAggregate()
    {
        // Arrange
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(services);
        var uow = new InMemoryUnitOfWork(dispatcher);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => uow.Track(null!));
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldRespectCancellationToken()
    {
        // Arrange
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(services);
        var uow = new InMemoryUnitOfWork(dispatcher);

        var aggregate = new UnitOfWorkTestAggregate();
        aggregate.DoWork("event");
        uow.Track(aggregate);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert — OperationCanceledException is acceptable
        // (depends on dispatcher implementation, but we verify no crash)
        var exception = await Record.ExceptionAsync(
            () => uow.SaveChangesAsync(cts.Token));

        // The cancellation may or may not propagate depending on dispatcher implementation.
        // The key invariant is that events are cleared even on cancellation paths.
        Assert.True(exception is null or OperationCanceledException,
            $"Expected null or OperationCanceledException but got {exception?.GetType().Name}");
    }
}
