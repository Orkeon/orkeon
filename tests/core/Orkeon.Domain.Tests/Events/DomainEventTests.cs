using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.Events;

namespace Orkeon.Domain.Tests.Events;

/// <summary>
/// Tests for DomainEvent following Clean Architecture principles.
/// Tests the base class functionality for domain events.
/// </summary>
public class DomainEventTests
{
    #region Test Domain Events

    private sealed record TestDomainEvent : DomainEvent
    {
        public required string AggregateId { get; init; }
        public required string TestData { get; init; }
    }

    private sealed record TestDomainEventWithVersion : DomainEvent
    {
        public required string AggregateId { get; init; }
        public required string TestData { get; init; }
        public override int Version => 2;
    }

    private sealed record TestDomainEventMinimal : DomainEvent
    {
        public required string AggregateId { get; init; }
    }

    #endregion

    [Fact]
    public void ShouldCreateDomainEvent_WhenConstructingWithValidParameters()
    {
        // Arrange
        var id = DomainEventId.Create();
        var occurredAt = DateTime.UtcNow;
        var aggregateId = "aggregate-123";
        var testData = "test data";

        // Act
        var domainEvent = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = aggregateId, TestData = testData };

        // Assert
        Assert.Equal(id, domainEvent.Id);
        Assert.Equal(occurredAt, domainEvent.OccurredAt);
        Assert.Equal(testData, domainEvent.TestData);
    }

    [Fact]
    public void ShouldReturnTypeNameCorrectly_WhenUsingEventName()
    {
        // Arrange
        var domainEvent = new TestDomainEvent { AggregateId = "agg-1", TestData = "test" };

        // Act
        var eventName = domainEvent.EventName;

        // Assert
        Assert.Equal("TestDomainEvent", eventName);
    }

    [Fact]
    public void ShouldReturnCorrectNames_WhenUsingEventNameWithDifferentEventTypes()
    {
        // Arrange
        var event1 = new TestDomainEvent { AggregateId = "agg-1", TestData = "test" };
        var event2 = new TestDomainEventMinimal { AggregateId = "agg-2" };

        // Act & Assert
        Assert.Equal("TestDomainEvent", event1.EventName);
        Assert.Equal("TestDomainEventMinimal", event2.EventName);
        Assert.NotEqual(event1.EventName, event2.EventName);
    }

    [Fact]
    public void ShouldReturnOne_WhenUsingVersionWithDefaultImplementation()
    {
        // Arrange
        var domainEvent = new TestDomainEvent { AggregateId = "agg-1", TestData = "test" };

        // Act
        var version = domainEvent.Version;

        // Assert
        Assert.Equal(1, version);
    }

    [Fact]
    public void ShouldReturnOverriddenValue_WhenUsingVersionWithOverriddenImplementation()
    {
        // Arrange
        var domainEvent = new TestDomainEventWithVersion { AggregateId = "agg-1", TestData = "test" };

        // Act
        var version = domainEvent.Version;

        // Assert
        Assert.Equal(2, version);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var id = DomainEventId.Create();
        var occurredAt = DateTime.UtcNow;
        var aggregateId = "aggregate-123";
        var testData = "test data";

        var event1 = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = aggregateId, TestData = testData };
        var event2 = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = aggregateId, TestData = testData };

        // Act & Assert
        Assert.True(event1.Equals(event2));
        Assert.True(event2.Equals(event1));
        Assert.True(event1 == event2);
        Assert.False(event1 != event2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentIds()
    {
        // Arrange
        var occurredAt = DateTime.UtcNow;
        var aggregateId = "aggregate-123";
        var testData = "test data";

        var event1 = new TestDomainEvent { OccurredAt = occurredAt, AggregateId = aggregateId, TestData = testData };
        var event2 = new TestDomainEvent { OccurredAt = occurredAt, AggregateId = aggregateId, TestData = testData };

        // Act & Assert — different auto-generated DomainEventIds
        Assert.False(event1.Equals(event2));
        Assert.False(event2.Equals(event1));
        Assert.False(event1 == event2);
        Assert.True(event1 != event2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentOccurredAt()
    {
        // Arrange
        var id = DomainEventId.Create();
        var aggregateId = "aggregate-123";
        var testData = "test data";

        var event1 = new TestDomainEvent { Id = id, OccurredAt = DateTime.UtcNow, AggregateId = aggregateId, TestData = testData };
        var event2 = new TestDomainEvent { Id = id, OccurredAt = DateTime.UtcNow.AddMinutes(1), AggregateId = aggregateId, TestData = testData };

        // Act & Assert
        Assert.False(event1.Equals(event2));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentAggregateId()
    {
        // Arrange
        var id = DomainEventId.Create();
        var occurredAt = DateTime.UtcNow;
        var testData = "test data";

        var event1 = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = "aggregate-123", TestData = testData };
        var event2 = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = "aggregate-456", TestData = testData };

        // Act & Assert
        Assert.False(event1.Equals(event2));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentTestData()
    {
        // Arrange
        var id = DomainEventId.Create();
        var occurredAt = DateTime.UtcNow;
        var aggregateId = "aggregate-123";

        var event1 = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = aggregateId, TestData = "test data 1" };
        var event2 = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = aggregateId, TestData = "test data 2" };

        // Act & Assert
        Assert.False(event1.Equals(event2));
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var id = DomainEventId.Create();
        var occurredAt = DateTime.UtcNow;
        var aggregateId = "aggregate-123";
        var testData = "test data";

        var event1 = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = aggregateId, TestData = testData };
        var event2 = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = aggregateId, TestData = testData };

        // Act
        var hash1 = event1.GetHashCode();
        var hash2 = event2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnDifferentHashCodes_WhenCallingGetHashCodeWithDifferentValues()
    {
        // Arrange
        var id = DomainEventId.Create();
        var occurredAt = DateTime.UtcNow;
        var aggregateId = "aggregate-123";

        var event1 = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = aggregateId, TestData = "test data 1" };
        var event2 = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = aggregateId, TestData = "test data 2" };

        // Act
        var hash1 = event1.GetHashCode();
        var hash2 = event2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("aggregate-1")]
    [InlineData("very-long-aggregate-identifier-with-many-characters")]
    [InlineData("123")]
    [InlineData("UPPERCASE_AGGREGATE")]
    public void ShouldAcceptAll_WhenConstructingWithVariousAggregateIds(string aggregateId)
    {
        // Act
        var domainEvent = new TestDomainEvent { AggregateId = aggregateId, TestData = "test" };

        // Assert
        Assert.NotNull(domainEvent.Id);
    }

    [Fact]
    public void ShouldGenerateNonNullId_WhenConstructingWithDefaults()
    {
        // Act
        var domainEvent = new TestDomainEvent { AggregateId = "agg-1", TestData = "test" };

        // Assert
        Assert.NotNull(domainEvent.Id);
    }

    [Fact]
    public void ShouldAcceptMinDateTime_WhenConstructingWithMinDateTime()
    {
        // Act
        var domainEvent = new TestDomainEvent { OccurredAt = DateTime.MinValue, AggregateId = "agg-1", TestData = "test" };

        // Assert
        Assert.Equal(DateTime.MinValue, domainEvent.OccurredAt);
    }

    [Fact]
    public void ShouldAcceptMaxDateTime_WhenConstructingWithMaxDateTime()
    {
        // Act
        var domainEvent = new TestDomainEvent { OccurredAt = DateTime.MaxValue, AggregateId = "agg-1", TestData = "test" };

        // Assert
        Assert.Equal(DateTime.MaxValue, domainEvent.OccurredAt);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingDomainEventInCollection()
    {
        // Arrange
        var events = new List<DomainEvent>
        {
            new TestDomainEvent { AggregateId = "agg-1", TestData = "data-1" },
            new TestDomainEventMinimal { AggregateId = "agg-2" },
            new TestDomainEventWithVersion { AggregateId = "agg-3", TestData = "data-3" }
        };

        // Act
        var eventNames = events.Select(e => e.EventName).ToList();
        var versions = events.Select(e => e.Version).ToList();

        // Assert
        Assert.Equal(3, events.Count);
        Assert.Contains("TestDomainEvent", eventNames);
        Assert.Contains("TestDomainEventMinimal", eventNames);
        Assert.Contains("TestDomainEventWithVersion", eventNames);

        Assert.Contains(1, versions);
        Assert.Contains(2, versions);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingDomainEventInHashSet()
    {
        // Arrange
        var hashSet = new HashSet<DomainEvent>();
        var id = DomainEventId.Create();
        var occurredAt = DateTime.UtcNow;
        var aggregateId = "agg-1";

        var event1 = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = aggregateId, TestData = "data" };
        var event2 = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = aggregateId, TestData = "data" }; // Same values
        var event3 = new TestDomainEvent { OccurredAt = occurredAt, AggregateId = aggregateId, TestData = "data" }; // Different ID

        // Act
        hashSet.Add(event1);
        hashSet.Add(event2); // Should not be added due to equality
        hashSet.Add(event3);

        // Assert
        Assert.Equal(2, hashSet.Count);
        Assert.Contains(event1, hashSet);
        Assert.Contains(event3, hashSet);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingDomainEventUsingPolymorphism()
    {
        // Arrange
        DomainEvent event1 = new TestDomainEvent { AggregateId = "agg-1", TestData = "data" };
        DomainEvent event2 = new TestDomainEventMinimal { AggregateId = "agg-2" };

        // Act & Assert
        Assert.Equal("TestDomainEvent", event1.EventName);
        Assert.Equal("TestDomainEventMinimal", event2.EventName);
        Assert.Equal(1, event1.Version);
        Assert.Equal(1, event2.Version);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenUsingDomainEventWithSameIdButDifferentTypes()
    {
        // Arrange
        var id = DomainEventId.Create();
        var occurredAt = DateTime.UtcNow;
        var aggregateId = "agg-1";

        var event1 = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = aggregateId, TestData = "data" };
        var event2 = new TestDomainEventMinimal { Id = id, OccurredAt = occurredAt, AggregateId = aggregateId };

        // Act & Assert
        Assert.False(event1.Equals(event2));
        Assert.False(event2.Equals(event1));
        Assert.NotEqual(event1.GetHashCode(), event2.GetHashCode());
    }

    [Fact]
    public void ShouldIncludeEventName_WhenUsingDomainEventToString()
    {
        // Arrange
        var domainEvent = new TestDomainEvent { AggregateId = "agg-1", TestData = "test" };

        // Act
        var stringRepresentation = domainEvent.ToString();

        // Assert
        Assert.Contains("TestDomainEvent", stringRepresentation);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingDomainEventWithNullAggregateId()
    {
        // Act & Assert
        var exception = Record.Exception(() =>
            new TestDomainEvent { AggregateId = null!, TestData = "test" });

        // The base record constructor should handle null values
        // This tests that the domain event doesn't crash with null
        Assert.Null(exception);
    }

    [Fact]
    public void ShouldNotThrow_WhenUsingDomainEventUsingPropertyAccess()
    {
        // Arrange
        var id = DomainEventId.Create();
        var occurredAt = DateTime.UtcNow;
        var aggregateId = "agg-123";
        var domainEvent = new TestDomainEvent { Id = id, OccurredAt = occurredAt, AggregateId = aggregateId, TestData = "test" };

        // Act & Assert - All property accesses should work
        Assert.Equal(id, domainEvent.Id);
        Assert.Equal(occurredAt, domainEvent.OccurredAt);
        Assert.Equal("TestDomainEvent", domainEvent.EventName);
        Assert.Equal(1, domainEvent.Version);
    }
}
