using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Domain.Tests.Fixtures;

namespace Orkeon.Domain.Tests.Common;

public class AggregateRootTests
{
    #region Test Helpers

    private sealed class TestEntityId : EntityId<TestEntityId>;

    private class TestAggregate : AggregateRoot<TestEntityId>
    {
        public TestAggregate() : base(TestEntityId.Create()) { }
        public TestAggregate(TestEntityId id) : base(id) { }
        public void PublicRaiseDomainEvent(DomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
        public void PublicMarkAsUpdated() => MarkAsUpdated();
    }

    private sealed record TestDomainEvent : DomainEvent
    {
        public required string AggregateId { get; init; }
        public required string TestData { get; init; }
    }

    #endregion

    #region Default Values Tests

    [Fact]
    public void ShouldInitializeDefaultValues_WhenConstructing()
    {
        var beforeCreation = DateTime.UtcNow;
        var aggregate = new TestAggregate();
        var afterCreation = DateTime.UtcNow;
        Assert.NotNull(aggregate.Id);
        Assert.InRange(aggregate.CreatedAt, beforeCreation, afterCreation);
        var timeDifference = Math.Abs((aggregate.UpdatedAt - aggregate.CreatedAt).TotalMilliseconds);
        Assert.True(timeDifference < 10);
        Assert.Equal(1, aggregate.Version);
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void ShouldBeUtc_WhenUsingCreatedAt()
    {
        var aggregate = new TestAggregate();
        Assert.Equal(DateTimeKind.Utc, aggregate.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, aggregate.UpdatedAt.Kind);
    }

    #endregion

    #region Domain Events Tests

    [Fact]
    public void ShouldAddEventToCollection_WhenUsingRaiseDomainEvent()
    {
        var aggregate = new TestAggregate();
        var evt = new TestDomainEvent { AggregateId = aggregate.Id.ToString(), TestData = "test data" };
        aggregate.PublicRaiseDomainEvent(evt);
        Assert.Single(aggregate.DomainEvents);
        Assert.Contains(evt, aggregate.DomainEvents);
    }

    [Fact]
    public void ShouldThrow_WhenUsingRaiseDomainEventWithNull()
    {
        var aggregate = new TestAggregate();
        var ex = Assert.Throws<ArgumentNullException>(() => aggregate.PublicRaiseDomainEvent(null!));
        Assert.Equal("domainEvent", ex.ParamName);
    }

    [Fact]
    public void ShouldMaintainOrder_WhenUsingRaiseDomainEventWithMultipleEvents()
    {
        var aggregate = new TestAggregate();
        var id = aggregate.Id.ToString();
        var e1 = new TestDomainEvent { AggregateId = id, TestData = "event1" };
        var e2 = new TestDomainEvent { AggregateId = id, TestData = "event2" };
        var e3 = new TestDomainEvent { AggregateId = id, TestData = "event3" };
        aggregate.PublicRaiseDomainEvent(e1);
        aggregate.PublicRaiseDomainEvent(e2);
        aggregate.PublicRaiseDomainEvent(e3);
        Assert.Equal(3, aggregate.DomainEvents.Count);
        Assert.Equal(e1, aggregate.DomainEvents[0]);
        Assert.Equal(e2, aggregate.DomainEvents[1]);
        Assert.Equal(e3, aggregate.DomainEvents[2]);
    }

    [Fact]
    public void ShouldReturnReadOnlyCollection_WhenUsingDomainEvents()
    {
        var aggregate = new TestAggregate();
        var evt = new TestDomainEvent { AggregateId = aggregate.Id.ToString(), TestData = "test" };
        aggregate.PublicRaiseDomainEvent(evt);
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<DomainEvent>>(aggregate.DomainEvents);
        Assert.Throws<NotSupportedException>(() => { (aggregate.DomainEvents as IList<DomainEvent>)?.Add(evt); });
    }

    [Fact]
    public void ShouldRemoveAllEvents_WhenClearingDomainEvents()
    {
        var aggregate = new TestAggregate();
        var id = aggregate.Id.ToString();
        aggregate.PublicRaiseDomainEvent(new TestDomainEvent { AggregateId = id, TestData = "1" });
        aggregate.PublicRaiseDomainEvent(new TestDomainEvent { AggregateId = id, TestData = "2" });
        aggregate.PublicRaiseDomainEvent(new TestDomainEvent { AggregateId = id, TestData = "3" });
        aggregate.ClearDomainEvents();
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void ShouldNotThrow_WhenClearingDomainEventsWhenEmpty()
    {
        var aggregate = new TestAggregate();
        Assert.Null(Record.Exception(() => aggregate.ClearDomainEvents()));
        Assert.Empty(aggregate.DomainEvents);
    }

    #endregion

    #region MarkAsUpdated Tests

    [Fact]
    public void ShouldUpdateTimestampAndVersion_WhenMarkingAsUpdated()
    {
        var aggregate = new TestAggregate();
        var originalUpdatedAt = aggregate.UpdatedAt;
        ClockAdvance.UntilStrictlyAfter(originalUpdatedAt);
        aggregate.PublicMarkAsUpdated();
        Assert.True(aggregate.UpdatedAt > originalUpdatedAt);
        Assert.Equal(2, aggregate.Version);
        Assert.Equal(DateTimeKind.Utc, aggregate.UpdatedAt.Kind);
    }

    [Fact]
    public void ShouldIncrementVersionCorrectly_WhenMarkingAsUpdatedWithMultipleTimes()
    {
        var aggregate = new TestAggregate();
        for (int i = 0; i < 5; i++) aggregate.PublicMarkAsUpdated();
        Assert.Equal(6, aggregate.Version);
    }

    [Fact]
    public void ShouldNotAffectCreatedAt_WhenMarkingAsUpdated()
    {
        var aggregate = new TestAggregate();
        var originalCreatedAt = aggregate.CreatedAt;
        aggregate.PublicMarkAsUpdated();
        Assert.Equal(originalCreatedAt, aggregate.CreatedAt);
    }

    #endregion

    #region Equality Tests

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameId()
    {
        var id = TestEntityId.Create();
        Assert.True(new TestAggregate(id).Equals(new TestAggregate(id)));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentId() =>
        Assert.False(new TestAggregate().Equals(new TestAggregate()));

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameReference()
    {
        var a = new TestAggregate();
        Assert.True(a.Equals(a));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithNull() =>
        Assert.False(new TestAggregate().Equals(null));

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentType() =>
        Assert.False(new TestAggregate().Equals("not an aggregate"));

    [Fact]
    public void ShouldReturnSameHash_WhenCallingGetHashCodeWithSameId()
    {
        var id = TestEntityId.Create();
        Assert.Equal(new TestAggregate(id).GetHashCode(), new TestAggregate(id).GetHashCode());
    }

    [Fact]
    public void ShouldReturnDifferentHash_WhenCallingGetHashCodeWithDifferentId() =>
        Assert.NotEqual(new TestAggregate().GetHashCode(), new TestAggregate().GetHashCode());

    #endregion

    #region Operator Tests

    [Fact]
    public void ShouldReturnTrue_WhenUsingValueEqualsWithSameId()
    {
        var id = TestEntityId.Create();
        Assert.True(new TestAggregate(id).Equals(new TestAggregate(id)));
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingValueEqualsWithDifferentId() =>
        Assert.False(new TestAggregate().Equals(new TestAggregate()));

    #endregion

    #region ToString Tests

    [Fact]
    public void ShouldReturnFormattedString_WhenCallingToString()
    {
        var result = new TestAggregate().ToString();
        Assert.StartsWith("TestAggregate [Id=", result);
        Assert.EndsWith("]", result);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldAggregateLifecycle_WhenUsingComplexScenario()
    {
        var aggregate = new TestAggregate();
        var id = aggregate.Id.ToString();
        aggregate.PublicRaiseDomainEvent(new TestDomainEvent { AggregateId = id, TestData = "OrderCreated" });
        aggregate.PublicMarkAsUpdated();
        aggregate.PublicRaiseDomainEvent(new TestDomainEvent { AggregateId = id, TestData = "ItemAdded" });
        aggregate.PublicMarkAsUpdated();
        aggregate.PublicRaiseDomainEvent(new TestDomainEvent { AggregateId = id, TestData = "OrderConfirmed" });

        Assert.Equal(3, aggregate.Version);
        Assert.Equal(3, aggregate.DomainEvents.Count);
        aggregate.ClearDomainEvents();
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldConcurrentAggregateHandling_WhenUsingComplexScenario()
    {
        var aggregates = Enumerable.Range(0, 10).Select(_ => new TestAggregate()).ToList();
        var tasks = aggregates.Select(agg => System.Threading.Tasks.Task.Run(() =>
        {
            for (int j = 0; j < 5; j++)
            {
                agg.PublicRaiseDomainEvent(new TestDomainEvent { AggregateId = agg.Id.ToString(), TestData = $"Event-{j}" });
                agg.PublicMarkAsUpdated();
            }
        })).ToArray();
        await System.Threading.Tasks.Task.WhenAll(tasks);
        foreach (var a in aggregates) { Assert.Equal(6, a.Version); Assert.Equal(5, a.DomainEvents.Count); }
        Assert.Equal(10, aggregates.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void ShouldDerivedAggregateTypes_WhenUsingComplexScenario()
    {
        var order = new OrderAggregate();
        var customer = new CustomerAggregate();
        Assert.False(order.Equals(customer));
        Assert.True(order.Equals(new OrderAggregate(order.Id)));
    }

    [Fact]
    public void ShouldEventSourcing_WhenUsingComplexScenario()
    {
        var aggregate = new TestAggregate();
        var id = aggregate.Id.ToString();
        var history = new List<(DateTime ts, DomainEvent evt, int ver)>();
        for (int i = 0; i < 10; i++)
        {
            var evt = new TestDomainEvent { AggregateId = id, TestData = $"State change {i}" };
            aggregate.PublicRaiseDomainEvent(evt);
            aggregate.PublicMarkAsUpdated();
            history.Add((DateTime.UtcNow, evt, aggregate.Version));
        }
        Assert.Equal(11, aggregate.Version);
        Assert.Equal(10, aggregate.DomainEvents.Count);
        for (int i = 1; i < history.Count; i++)
        {
            Assert.True(history[i].ts >= history[i - 1].ts);
            Assert.Equal(history[i - 1].ver + 1, history[i].ver);
        }
    }

    [Fact]
    public void ShouldOptimisticConcurrency_WhenUsingComplexScenario()
    {
        var id = TestEntityId.Create();
        var u1 = new TestAggregate(id);
        var u2 = new TestAggregate(id);
        u1.PublicMarkAsUpdated();
        u2.PublicMarkAsUpdated();
        Assert.Equal(2, u1.Version);
        Assert.Equal(2, u2.Version);
        Assert.NotEqual(u1.UpdatedAt, u2.UpdatedAt);
    }

    #endregion

    #region Test Helper Classes

    private sealed class OrderEntityId : EntityId<OrderEntityId>;
    private class OrderAggregate : AggregateRoot<OrderEntityId>
    {
        public OrderAggregate() : base(OrderEntityId.Create()) { }
        public OrderAggregate(OrderEntityId id) : base(id) { }
    }

    private sealed class CustomerEntityId : EntityId<CustomerEntityId>;
    private class CustomerAggregate : AggregateRoot<CustomerEntityId>
    {
        public CustomerAggregate() : base(CustomerEntityId.Create()) { }
    }

    #endregion
}
