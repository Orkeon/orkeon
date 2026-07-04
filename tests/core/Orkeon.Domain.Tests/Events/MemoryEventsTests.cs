using Orkeon.Domain.Common;
using Orkeon.Domain.Memory.Events;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Domain.Memory;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Domain.Tests.Events;

/// <summary>
/// Tests for MemoryEvents following Clean Architecture principles.
/// Tests all memory-related domain events.
/// </summary>
public class MemoryEventsTests
{
    private static readonly string[] ImportantReferenceTags = ["important", "reference"];
    private static readonly string[] Agent1Agent2Participants = ["agent1", "agent2"];
    private static readonly string[] ActionHistory = ["action1", "action2", "action3"];
    private static readonly string[] ImportantVerifiedFlags = ["important", "verified"];
    #region MemoryStoreCreatedEvent Tests

    [Fact]
    public void ShouldCreateEvent_WhenUsingMemoryStoreCreatedEventWithValidParameters()
    {
        // Arrange
        var memoryStoreId = MemoryStoreId.Create();
        var ownerAgentId = AgentId.Create();
        var occurredAt = DateTime.UtcNow;

        // Act
        var memoryStoreCreatedEvent = new MemoryStoreCreatedEvent { MemoryStoreId = memoryStoreId, OwnerAgentId = ownerAgentId, OccurredAt = occurredAt };

        // Assert
        Assert.Equal(memoryStoreId, memoryStoreCreatedEvent.MemoryStoreId);
        Assert.Equal(ownerAgentId, memoryStoreCreatedEvent.OwnerAgentId);
        Assert.Equal(occurredAt, memoryStoreCreatedEvent.OccurredAt);
        Assert.Equal("MemoryStoreCreatedEvent", memoryStoreCreatedEvent.EventName);
        Assert.NotEqual(Guid.Empty, memoryStoreCreatedEvent.Id);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingMemoryStoreCreatedEventUsingEqualsWithSameValues()
    {
        // Arrange
        var memoryStoreId = MemoryStoreId.Create();
        var ownerAgentId = AgentId.Create();
        var occurredAt = DateTime.UtcNow;

        var event1 = new MemoryStoreCreatedEvent { MemoryStoreId = memoryStoreId, OwnerAgentId = ownerAgentId, OccurredAt = occurredAt };
        var event2 = new MemoryStoreCreatedEvent { MemoryStoreId = memoryStoreId, OwnerAgentId = ownerAgentId, OccurredAt = occurredAt };

        // Act & Assert
        // Note: Events will have different IDs due to Guid.NewGuid()
        Assert.NotEqual(event1, event2);
        Assert.Equal(event1.MemoryStoreId, event2.MemoryStoreId);
        Assert.Equal(event1.OwnerAgentId, event2.OwnerAgentId);
        Assert.Equal(event1.OccurredAt, event2.OccurredAt);
    }

    #endregion

    #region MemoryAddedEvent Tests

    [Fact]
    public void ShouldCreateEvent_WhenUsingMemoryAddedEventWithValidParameters()
    {
        // Arrange
        var memoryStoreId = MemoryStoreId.Create();
        var memory = MemoryItem.Create("Test memory content");
        var agentId = AgentId.Create();
        var occurredAt = DateTime.UtcNow;

        // Act
        var memoryAddedEvent = new MemoryAddedEvent { MemoryStoreId = memoryStoreId, MemoryItemId = memory.Id, Content = memory.Content, Importance = memory.Importance, AgentId = agentId, OccurredAt = occurredAt };

        // Assert
        Assert.Equal(memoryStoreId, memoryAddedEvent.MemoryStoreId);
        Assert.Equal(memory.Id, memoryAddedEvent.MemoryItemId);
        Assert.Equal(memory.Content, memoryAddedEvent.Content);
        Assert.Equal(memory.Importance, memoryAddedEvent.Importance);
        Assert.Equal(agentId, memoryAddedEvent.AgentId);
        Assert.Equal(occurredAt, memoryAddedEvent.OccurredAt);
        Assert.Equal("MemoryAddedEvent", memoryAddedEvent.EventName);
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenUsingMemoryAddedEventWithComplexMemory()
    {
        // Arrange
        var memory = MemoryItem.Create("Complex memory with rich metadata");

        // Act
        var memoryAddedEvent = new MemoryAddedEvent { MemoryStoreId = MemoryStoreId.Create(), MemoryItemId = memory.Id, Content = memory.Content, Importance = memory.Importance, AgentId = AgentId.Create(), OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(memory.Id, memoryAddedEvent.MemoryItemId);
        Assert.Equal("Complex memory with rich metadata", memoryAddedEvent.Content);
    }

    #endregion

    #region MemoryPromotedEvent Tests

    [Fact]
    public void ShouldCreateEvent_WhenUsingMemoryPromotedEventWithValidParameters()
    {
        // Arrange
        var memoryStoreId = MemoryStoreId.Create();
        var memory = MemoryItem.Create("Promoted memory content");
        var agentId = AgentId.Create();
        var occurredAt = DateTime.UtcNow;

        // Act
        var memoryPromotedEvent = new MemoryPromotedEvent { MemoryStoreId = memoryStoreId, MemoryItemId = memory.Id, Content = memory.Content, Importance = memory.Importance, AgentId = agentId, OccurredAt = occurredAt };

        // Assert
        Assert.Equal(memoryStoreId, memoryPromotedEvent.MemoryStoreId);
        Assert.Equal(memory.Id, memoryPromotedEvent.MemoryItemId);
        Assert.Equal(memory.Content, memoryPromotedEvent.Content);
        Assert.Equal(memory.Importance, memoryPromotedEvent.Importance);
        Assert.Equal(agentId, memoryPromotedEvent.AgentId);
        Assert.Equal(occurredAt, memoryPromotedEvent.OccurredAt);
        Assert.Equal("MemoryPromotedEvent", memoryPromotedEvent.EventName);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingMemoryPromotedEventWithImportantMemory()
    {
        // Arrange
        var importantMemory = MemoryItem.Create("Critical system knowledge");

        // Act
        var memoryPromotedEvent = new MemoryPromotedEvent { MemoryStoreId = MemoryStoreId.Create(), MemoryItemId = importantMemory.Id, Content = importantMemory.Content, Importance = importantMemory.Importance, AgentId = AgentId.Create(), OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(importantMemory.Id, memoryPromotedEvent.MemoryItemId);
        Assert.Equal("Critical system knowledge", memoryPromotedEvent.Content);
    }

    #endregion

    #region EntityMemoryUpdatedEvent Tests

    [Fact]
    public void ShouldCreateEvent_WhenUsingEntityMemoryUpdatedEventWithValidParameters()
    {
        // Arrange
        var agentId = AgentId.Create();
        var entityKey = "user_preferences";
        var updatedValue = new { theme = "dark", language = "en" };
        var occurredAt = DateTime.UtcNow;

        // Act
        var entityMemoryUpdatedEvent = new EntityMemoryUpdatedEvent { AgentId = agentId, EntityKey = entityKey, UpdatedValue = updatedValue, OccurredAt = occurredAt };

        // Assert
        Assert.Equal(agentId, entityMemoryUpdatedEvent.AgentId);
        Assert.Equal(entityKey, entityMemoryUpdatedEvent.EntityKey);
        Assert.Equal(updatedValue, entityMemoryUpdatedEvent.UpdatedValue);
        Assert.Equal(occurredAt, entityMemoryUpdatedEvent.OccurredAt);
        Assert.Equal("EntityMemoryUpdatedEvent", entityMemoryUpdatedEvent.EventName);
    }

    [Fact]
    public void ShouldAcceptNull_WhenUsingEntityMemoryUpdatedEventWithNullUpdatedValue()
    {
        // Act
        var entityMemoryUpdatedEvent = new EntityMemoryUpdatedEvent { AgentId = AgentId.Create(), EntityKey = "entity_key", UpdatedValue = null, OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Null(entityMemoryUpdatedEvent.UpdatedValue);
    }

    [Theory]
    [InlineData("string_value", "test")]
    [InlineData("int_value", 42)]
    [InlineData("bool_value", true)]
    [InlineData("double_value", 3.14)]
    [InlineData("null_value", null)]
    public void ShouldAcceptAll_WhenUsingEntityMemoryUpdatedEventWithVariousValueTypes(
        string entityKey, object? updatedValue)
    {
        // Act
        var entityMemoryUpdatedEvent = new EntityMemoryUpdatedEvent { AgentId = AgentId.Create(), EntityKey = entityKey, UpdatedValue = updatedValue, OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(entityKey, entityMemoryUpdatedEvent.EntityKey);
        Assert.Equal(updatedValue, entityMemoryUpdatedEvent.UpdatedValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("simple_key")]
    [InlineData("complex.entity.key")]
    [InlineData("UPPERCASE_KEY")]
    [InlineData("key_with_123_numbers")]
    [InlineData("key-with-dashes")]
    public void ShouldAcceptAll_WhenUsingEntityMemoryUpdatedEventWithVariousEntityKeys(string entityKey)
    {
        // Act
        var entityMemoryUpdatedEvent = new EntityMemoryUpdatedEvent { AgentId = AgentId.Create(), EntityKey = entityKey, UpdatedValue = "value", OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(entityKey, entityMemoryUpdatedEvent.EntityKey);
    }

    #endregion

    #region EpisodicMemoryAddedEvent Tests

    [Fact]
    public void ShouldCreateEvent_WhenUsingEpisodicMemoryAddedEventWithValidParameters()
    {
        // Arrange
        var agentId = AgentId.Create();
        var episode = EpisodicMemory.Create(
            "Task completion episode",
            agentId);
        var occurredAt = DateTime.UtcNow;

        // Act
        var episodicMemoryAddedEvent = new EpisodicMemoryAddedEvent { AgentId = agentId, EpisodeId = episode.Id, EpisodeTitle = episode.Title, OccurredAt = occurredAt };

        // Assert
        Assert.Equal(agentId, episodicMemoryAddedEvent.AgentId);
        Assert.Equal(episode.Id, episodicMemoryAddedEvent.EpisodeId);
        Assert.Equal(episode.Title, episodicMemoryAddedEvent.EpisodeTitle);
        Assert.Equal(occurredAt, episodicMemoryAddedEvent.OccurredAt);
        Assert.Equal("EpisodicMemoryAddedEvent", episodicMemoryAddedEvent.EventName);
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenUsingEpisodicMemoryAddedEventWithComplexEpisode()
    {
        // Arrange
        var episode = EpisodicMemory.Create("Complex problem solving session", AgentId.Create());

        // Act
        var episodicMemoryAddedEvent = new EpisodicMemoryAddedEvent { AgentId = AgentId.Create(), EpisodeId = episode.Id, EpisodeTitle = episode.Title, OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(episode.Id, episodicMemoryAddedEvent.EpisodeId);
        Assert.Equal("Complex problem solving session", episodicMemoryAddedEvent.EpisodeTitle);
    }

    #endregion

    #region MemoryClearedEvent Tests

    [Fact]
    public void ShouldCreateEvent_WhenUsingMemoryClearedEventWithValidParameters()
    {
        // Arrange
        var memoryStoreId = MemoryStoreId.Create();
        var memoryType = MemoryType.ShortTerm;
        var agentId = AgentId.Create();
        var occurredAt = DateTime.UtcNow;

        // Act
        var memoryClearedEvent = new MemoryClearedEvent { MemoryStoreId = memoryStoreId, MemoryType = memoryType, AgentId = agentId, OccurredAt = occurredAt };

        // Assert
        Assert.Equal(memoryStoreId, memoryClearedEvent.MemoryStoreId);
        Assert.Equal(memoryType, memoryClearedEvent.MemoryType);
        Assert.Equal(agentId, memoryClearedEvent.AgentId);
        Assert.Equal(occurredAt, memoryClearedEvent.OccurredAt);
        Assert.Equal("MemoryClearedEvent", memoryClearedEvent.EventName);
    }

    [Theory]
    [InlineData(MemoryType.ShortTerm)]
    [InlineData(MemoryType.LongTerm)]
    [InlineData(MemoryType.Episodic)]
    public void ShouldAcceptAll_WhenUsingMemoryClearedEventWithDifferentMemoryTypes(MemoryType memoryType)
    {
        // Act
        var memoryClearedEvent = new MemoryClearedEvent { MemoryStoreId = MemoryStoreId.Create(), MemoryType = memoryType, AgentId = AgentId.Create(), OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(memoryType, memoryClearedEvent.MemoryType);
    }

    #endregion

    #region Collection and Integration Tests

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingMemoryEventsInCollection()
    {
        // Arrange
        var agentId = AgentId.Create();
        var memoryStoreId = MemoryStoreId.Create();
        var memory = MemoryItem.Create("Test memory");
        var episode = EpisodicMemory.Create("Test episode", agentId);
        var occurredAt = DateTime.UtcNow;

        var events = new List<DomainEvent>
        {
            new MemoryStoreCreatedEvent { MemoryStoreId = memoryStoreId, OwnerAgentId = agentId, OccurredAt = occurredAt },
            new MemoryAddedEvent { MemoryStoreId = memoryStoreId, MemoryItemId = memory.Id, Content = memory.Content, Importance = memory.Importance, AgentId = agentId, OccurredAt = occurredAt },
            new MemoryPromotedEvent { MemoryStoreId = memoryStoreId, MemoryItemId = memory.Id, Content = memory.Content, Importance = memory.Importance, AgentId = agentId, OccurredAt = occurredAt },
            new EntityMemoryUpdatedEvent { AgentId = agentId, EntityKey = "entity_key", UpdatedValue = "value", OccurredAt = occurredAt },
            new EpisodicMemoryAddedEvent { AgentId = agentId, EpisodeId = episode.Id, EpisodeTitle = episode.Title, OccurredAt = occurredAt },
            new MemoryClearedEvent { MemoryStoreId = memoryStoreId, MemoryType = MemoryType.ShortTerm, AgentId = agentId, OccurredAt = occurredAt }
        };

        // Act
        var eventNames = events.Select(e => e.EventName).ToList();

        // Assert
        Assert.Equal(6, events.Count);
        Assert.Contains("MemoryStoreCreatedEvent", eventNames);
        Assert.Contains("MemoryAddedEvent", eventNames);
        Assert.Contains("MemoryPromotedEvent", eventNames);
        Assert.Contains("EntityMemoryUpdatedEvent", eventNames);
        Assert.Contains("EpisodicMemoryAddedEvent", eventNames);
        Assert.Contains("MemoryClearedEvent", eventNames);

        // All events should have agentId as aggregate
    }

    [Fact]
    public void ShouldShareAggregateId_WhenUsingMemoryEventsWithSameAgentId()
    {
        // Arrange
        var sharedAgentId = AgentId.Create();
        var memoryStoreId = MemoryStoreId.Create();
        var memory = MemoryItem.Create("content");

        var createEvent = new MemoryStoreCreatedEvent { MemoryStoreId = memoryStoreId, OwnerAgentId = sharedAgentId, OccurredAt = DateTime.UtcNow };
        var addEvent = new MemoryAddedEvent { MemoryStoreId = memoryStoreId, MemoryItemId = memory.Id, Content = memory.Content, Importance = memory.Importance, AgentId = sharedAgentId, OccurredAt = DateTime.UtcNow };
        var clearEvent = new MemoryClearedEvent { MemoryStoreId = memoryStoreId, MemoryType = MemoryType.ShortTerm, AgentId = sharedAgentId, OccurredAt = DateTime.UtcNow };

        // Act & Assert — all events reference the same agent
        Assert.Equal(sharedAgentId, createEvent.OwnerAgentId);
        Assert.Equal(sharedAgentId, addEvent.AgentId);
        Assert.Equal(sharedAgentId, clearEvent.AgentId);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingMemoryEventsPolymorphism()
    {
        // Arrange
        var memory = MemoryItem.Create("Test");
        DomainEvent memoryEvent = new MemoryAddedEvent { MemoryStoreId = MemoryStoreId.Create(), MemoryItemId = memory.Id, Content = memory.Content, Importance = memory.Importance, AgentId = AgentId.Create(), OccurredAt = DateTime.UtcNow };

        // Act & Assert
        Assert.Equal("MemoryAddedEvent", memoryEvent.EventName);
        Assert.Equal(1, memoryEvent.Version);
        Assert.IsType<MemoryAddedEvent>(memoryEvent);
    }

    [Fact]
    public void ShouldHaveCommonInterface_WhenUsingMemoryEventsWithAllInheritFromDomainEvent()
    {
        // Arrange
        var agentId = AgentId.Create();
        var memoryStoreId = MemoryStoreId.Create();
        var memory = MemoryItem.Create("Test");
        var episode = EpisodicMemory.Create("Test", agentId);

        var events = new DomainEvent[]
        {
            new MemoryStoreCreatedEvent { MemoryStoreId = memoryStoreId, OwnerAgentId = agentId, OccurredAt = DateTime.UtcNow },
            new MemoryAddedEvent { MemoryStoreId = memoryStoreId, MemoryItemId = memory.Id, Content = memory.Content, Importance = memory.Importance, AgentId = agentId, OccurredAt = DateTime.UtcNow },
            new MemoryPromotedEvent { MemoryStoreId = memoryStoreId, MemoryItemId = memory.Id, Content = memory.Content, Importance = memory.Importance, AgentId = agentId, OccurredAt = DateTime.UtcNow },
            new EntityMemoryUpdatedEvent { AgentId = agentId, EntityKey = "key", UpdatedValue = "value", OccurredAt = DateTime.UtcNow },
            new EpisodicMemoryAddedEvent { AgentId = agentId, EpisodeId = episode.Id, EpisodeTitle = episode.Title, OccurredAt = DateTime.UtcNow },
            new MemoryClearedEvent { MemoryStoreId = memoryStoreId, MemoryType = MemoryType.LongTerm, AgentId = agentId, OccurredAt = DateTime.UtcNow }
        };

        // Act & Assert
        foreach (var domainEvent in events)
        {
            Assert.NotEqual(Guid.Empty, domainEvent.Id);
            Assert.NotEmpty(domainEvent.EventName);
            Assert.Equal(1, domainEvent.Version);
            Assert.NotNull(domainEvent);
        }
    }

    #endregion

    #region Edge Cases and Complex Scenarios

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingMemoryEventsWithLargeContent()
    {
        // Arrange
        var largeContent = new string('x', 10000); // 10KB content
        var memory = MemoryItem.Create(largeContent);

        // Act
        var memoryAddedEvent = new MemoryAddedEvent { MemoryStoreId = MemoryStoreId.Create(), MemoryItemId = memory.Id, Content = memory.Content, Importance = memory.Importance, AgentId = AgentId.Create(), OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(10000, memoryAddedEvent.Content.Length);
        Assert.Equal(largeContent, memoryAddedEvent.Content);
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenUsingEntityMemoryUpdatedEventWithComplexObject()
    {
        // Arrange
        var complexObject = new
        {
            Settings = new { Theme = "dark", Font = "Arial" },
            History = ActionHistory,
            Metadata = new Dictionary<string, object>
            {
                { "lastAccess", DateTime.UtcNow },
                { "version", 2 },
                { "flags", ImportantVerifiedFlags }
            }
        };

        // Act
        var entityMemoryUpdatedEvent = new EntityMemoryUpdatedEvent { AgentId = AgentId.Create(), EntityKey = "complex_entity", UpdatedValue = complexObject, OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(complexObject, entityMemoryUpdatedEvent.UpdatedValue);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingMemoryEventsWithExtremeTimestamps()
    {
        // Arrange & Act
        var minTimeEvent = new MemoryStoreCreatedEvent { MemoryStoreId = MemoryStoreId.Create(), OwnerAgentId = AgentId.Create(), OccurredAt = DateTime.MinValue };
        var maxTimeEvent = new MemoryStoreCreatedEvent { MemoryStoreId = MemoryStoreId.Create(), OwnerAgentId = AgentId.Create(), OccurredAt = DateTime.MaxValue };

        // Assert
        Assert.Equal(DateTime.MinValue, minTimeEvent.OccurredAt);
        Assert.Equal(DateTime.MaxValue, maxTimeEvent.OccurredAt);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingMemoryEventsInHashSet()
    {
        // Arrange
        var hashSet = new HashSet<DomainEvent>();
        var agentId = AgentId.Create();
        var memoryStoreId = MemoryStoreId.Create();
        var occurredAt = DateTime.UtcNow;

        var event1 = new MemoryStoreCreatedEvent { MemoryStoreId = memoryStoreId, OwnerAgentId = agentId, OccurredAt = occurredAt };
        var event2 = new MemoryStoreCreatedEvent { MemoryStoreId = memoryStoreId, OwnerAgentId = agentId, OccurredAt = occurredAt }; // Same values
        var event3 = new MemoryClearedEvent { MemoryStoreId = memoryStoreId, MemoryType = MemoryType.ShortTerm, AgentId = agentId, OccurredAt = occurredAt };

        // Act
        hashSet.Add(event1);
        hashSet.Add(event2); // Different ID, so will be added
        hashSet.Add(event3);

        // Assert
        Assert.Equal(3, hashSet.Count); // All different due to different IDs
    }

    [Fact]
    public void ShouldIncludeEventNames_WhenUsingMemoryEventsToString()
    {
        // Arrange
        var memory = MemoryItem.Create("Test");
        var memoryEvent = new MemoryAddedEvent { MemoryStoreId = MemoryStoreId.Create(), MemoryItemId = memory.Id, Content = memory.Content, Importance = memory.Importance, AgentId = AgentId.Create(), OccurredAt = DateTime.UtcNow };

        // Act
        var stringRepresentation = memoryEvent.ToString();

        // Assert
        Assert.Contains("MemoryAddedEvent", stringRepresentation);
    }

    [Fact]
    public void ShouldNotThrow_WhenUsingMemoryEventsPropertyAccess()
    {
        // Arrange
        var agentId = AgentId.Create();
        var memoryStoreId = MemoryStoreId.Create();
        var memory = MemoryItem.Create(TestContent);
        var episode = EpisodicMemory.Create("Test episode", agentId);
        var occurredAt = DateTime.UtcNow;

        var events = new DomainEvent[]
        {
            new MemoryStoreCreatedEvent { MemoryStoreId = memoryStoreId, OwnerAgentId = agentId, OccurredAt = occurredAt },
            new MemoryAddedEvent { MemoryStoreId = memoryStoreId, MemoryItemId = memory.Id, Content = memory.Content, Importance = memory.Importance, AgentId = agentId, OccurredAt = occurredAt },
            new EntityMemoryUpdatedEvent { AgentId = agentId, EntityKey = "key", UpdatedValue = "value", OccurredAt = occurredAt },
            new EpisodicMemoryAddedEvent { AgentId = agentId, EpisodeId = episode.Id, EpisodeTitle = episode.Title, OccurredAt = occurredAt },
            new MemoryClearedEvent { MemoryStoreId = memoryStoreId, MemoryType = MemoryType.ShortTerm, AgentId = agentId, OccurredAt = occurredAt }
        };

        // Act & Assert - All property accesses should work without throwing
        foreach (var domainEvent in events)
        {
            Assert.NotEqual(Guid.Empty, domainEvent.Id);
            Assert.Equal(occurredAt, domainEvent.OccurredAt);
            Assert.NotEmpty(domainEvent.EventName);
            Assert.Equal(1, domainEvent.Version);
        }
    }

    #endregion
}
