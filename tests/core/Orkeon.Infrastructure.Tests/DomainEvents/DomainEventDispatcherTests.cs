using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Infrastructure.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkeon.Infrastructure.Tests.DomainEvents;

public sealed record TestEvent(string Data) : DomainEvent;

public sealed record AnotherTestEvent(int Value) : DomainEvent;

public sealed class RecordingHandler : IDomainEventHandler<TestEvent>
{
    public List<TestEvent> HandledEvents { get; } = [];

    public Task HandleAsync(TestEvent domainEvent, CancellationToken cancellationToken = default)
    {
        HandledEvents.Add(domainEvent);
        return Task.CompletedTask;
    }
}

public sealed class SecondRecordingHandler : IDomainEventHandler<TestEvent>
{
    public List<TestEvent> HandledEvents { get; } = [];

    public Task HandleAsync(TestEvent domainEvent, CancellationToken cancellationToken = default)
    {
        HandledEvents.Add(domainEvent);
        return Task.CompletedTask;
    }
}

public sealed class TestAggregateId : EntityId<TestAggregateId>;

public sealed class TestAggregate : AggregateRoot<TestAggregateId>
{
    public TestAggregate() : base(TestAggregateId.Create()) { }

    public void DoSomething(string data)
    {
        RaiseDomainEvent(new TestEvent(data));
    }

    public void DoAnotherThing(int value)
    {
        RaiseDomainEvent(new AnotherTestEvent(value));
    }
}

public class DomainEventDispatcherTests
{
    private readonly ILogger<DomainEventDispatcher> _logger =
        NullLogger<DomainEventDispatcher>.Instance;

    [Fact]
    public async Task DispatchAsync_ShouldInvokeRegisteredHandler()
    {
        // Arrange
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handler);
        var sp = services.BuildServiceProvider();

        var dispatcher = new DomainEventDispatcher(sp, _logger);
        var evt = new TestEvent("hello");

        // Act
        await dispatcher.DispatchAsync(evt, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler.HandledEvents);
        Assert.Equal("hello", handler.HandledEvents[0].Data);
    }

    [Fact]
    public async Task DispatchAsync_ShouldHandleNoHandlerGracefully()
    {
        // Arrange — no handler registered for AnotherTestEvent
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var dispatcher = new DomainEventDispatcher(sp, _logger);
        var evt = new AnotherTestEvent(42);

        // Act & Assert — should not throw
        var exception = await Record.ExceptionAsync(() => dispatcher.DispatchAsync(evt, TestContext.Current.CancellationToken));
        Assert.Null(exception);
    }

    [Fact]
    public async Task DispatchManyAsync_ShouldDispatchMultipleEventsInOrder()
    {
        // Arrange
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handler);
        var sp = services.BuildServiceProvider();

        var dispatcher = new DomainEventDispatcher(sp, _logger);
        var events = new DomainEvent[]
        {
            new TestEvent("first"),
            new TestEvent("second"),
            new TestEvent("third"),
        };

        // Act
        await dispatcher.DispatchManyAsync(events, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, handler.HandledEvents.Count);
        Assert.Equal("first", handler.HandledEvents[0].Data);
        Assert.Equal("second", handler.HandledEvents[1].Data);
        Assert.Equal("third", handler.HandledEvents[2].Data);
    }

    [Fact]
    public async Task DispatchAsync_ShouldInvokeMultipleHandlersForSameEvent()
    {
        // Arrange
        var handler1 = new RecordingHandler();
        var handler2 = new SecondRecordingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handler1);
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handler2);
        var sp = services.BuildServiceProvider();

        var dispatcher = new DomainEventDispatcher(sp, _logger);
        var evt = new TestEvent("multi");

        // Act
        await dispatcher.DispatchAsync(evt, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(handler1.HandledEvents);
        Assert.Single(handler2.HandledEvents);
    }

    [Fact]
    public async Task DispatchAndClearEventsAsync_ShouldDispatchAndClearAggregate()
    {
        // Arrange
        var handler = new RecordingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handler);
        var sp = services.BuildServiceProvider();

        var dispatcher = new DomainEventDispatcher(sp, _logger);
        var aggregate = new TestAggregate();
        aggregate.DoSomething("event1");
        aggregate.DoSomething("event2");

        Assert.Equal(2, aggregate.DomainEvents.Count);

        // Act
        await AggregateEventHelper.DispatchAndClearEventsAsync(aggregate, dispatcher, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, handler.HandledEvents.Count);
        Assert.Empty(aggregate.DomainEvents);
    }
}
