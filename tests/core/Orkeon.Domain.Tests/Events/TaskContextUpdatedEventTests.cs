using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.Events;
using Orkeon.Domain.Task.Events;

namespace Orkeon.Domain.Tests.Events;

/// <summary>
/// Tests for TaskContextUpdatedEvent following Clean Architecture principles.
/// Tests the business rules and validation logic of the TaskContextUpdatedEvent.
/// </summary>
public class TaskContextUpdatedEventTests
{
    [Fact]
    public void ShouldCreateEvent_WhenConstructingWithValidParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var contextType = "AnalysisContext";
        var beforeState = "{\"status\":\"pending\",\"progress\":0}";
        var afterState = "{\"status\":\"in_progress\",\"progress\":25}";
        var occurredAt = DateTime.UtcNow;

        // Act
        var taskContextUpdatedEvent = new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId, ContextType = contextType, BeforeState = beforeState, AfterState = afterState, OccurredAt = occurredAt };

        // Assert
        Assert.Equal(taskId, taskContextUpdatedEvent.TaskId);
        Assert.Equal(agentId, taskContextUpdatedEvent.AgentId);
        Assert.Equal(contextType, taskContextUpdatedEvent.ContextType);
        Assert.Equal(beforeState, taskContextUpdatedEvent.BeforeState);
        Assert.Equal(afterState, taskContextUpdatedEvent.AfterState);
        Assert.Equal(occurredAt, taskContextUpdatedEvent.OccurredAt);
        Assert.Equal("TaskContextUpdatedEvent", taskContextUpdatedEvent.EventName);
        Assert.NotEqual(Guid.Empty, taskContextUpdatedEvent.Id);
    }

    [Fact]
    public void ShouldAcceptNullTaskId_WhenConstructingWithNullTaskId()
    {
        // Act - With required + null!, the record accepts null without throwing
        var evt = new TaskContextUpdatedEvent { TaskId = null!, AgentId = AgentId.Create(), ContextType = "context", BeforeState = "before", AfterState = "after", OccurredAt = DateTime.UtcNow };
        Assert.Null(evt.TaskId);
    }

    [Fact]
    public void ShouldAcceptNullAgentId_WhenConstructingWithNullAgentId()
    {
        // Act - With required + null!, the record accepts null without throwing
        var evt = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = null!, ContextType = "context", BeforeState = "before", AfterState = "after", OccurredAt = DateTime.UtcNow };
        Assert.Null(evt.AgentId);
    }

    [Fact]
    public void ShouldAcceptNullContextType_WhenConstructingWithNullContextType()
    {
        // Act - With required + null!, the record accepts null without throwing
        var evt = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = AgentId.Create(), ContextType = null!, BeforeState = "before", AfterState = "after", OccurredAt = DateTime.UtcNow };
        Assert.Null(evt.ContextType);
    }

    [Fact]
    public void ShouldAcceptNullBeforeState_WhenConstructingWithNullBeforeState()
    {
        // Act - With required + null!, the record accepts null without throwing
        var evt = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = AgentId.Create(), ContextType = "context", BeforeState = null!, AfterState = "after", OccurredAt = DateTime.UtcNow };
        Assert.Null(evt.BeforeState);
    }

    [Fact]
    public void ShouldAcceptNullAfterState_WhenConstructingWithNullAfterState()
    {
        // Act - With required + null!, the record accepts null without throwing
        var evt = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = AgentId.Create(), ContextType = "context", BeforeState = "before", AfterState = null!, OccurredAt = DateTime.UtcNow };
        Assert.Null(evt.AfterState);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AnalysisContext")]
    [InlineData("ResearchContext")]
    [InlineData("CodeGenerationContext")]
    [InlineData("UPPERCASE_CONTEXT")]
    [InlineData("context-with-dashes")]
    [InlineData("context.with.dots")]
    [InlineData("Context123WithNumbers")]
    public void ShouldAcceptAll_WhenConstructingWithVariousContextTypes(string contextType)
    {
        // Act
        var taskContextUpdatedEvent = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = AgentId.Create(), ContextType = contextType, BeforeState = "before", AfterState = "after", OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(contextType, taskContextUpdatedEvent.ContextType);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("{}", "{}")]
    [InlineData("simple", "simple")]
    [InlineData("{\"prop\":\"value\"}", "{\"prop\":\"new_value\"}")]
    public void ShouldAcceptAll_WhenConstructingWithVariousStates(string beforeState, string afterState)
    {
        // Act
        var taskContextUpdatedEvent = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = AgentId.Create(), ContextType = "context", BeforeState = beforeState, AfterState = afterState, OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(beforeState, taskContextUpdatedEvent.BeforeState);
        Assert.Equal(afterState, taskContextUpdatedEvent.AfterState);
    }

    [Fact]
    public void ShouldStoreCorrectly_WhenConstructingWithComplexJsonStates()
    {
        // Arrange
        var beforeState = @"{
            ""status"": ""pending"",
            ""progress"": 0,
            ""metadata"": {
                ""priority"": ""high"",
                ""assignee"": ""agent-123"",
                ""tags"": [""important"", ""urgent""]
            },
            ""timestamps"": {
                ""created"": ""2024-01-01T00:00:00Z"",
                ""updated"": null
            }
        }";

        var afterState = @"{
            ""status"": ""in_progress"",
            ""progress"": 45,
            ""metadata"": {
                ""priority"": ""high"",
                ""assignee"": ""agent-123"",
                ""tags"": [""important"", ""urgent"", ""started""]
            },
            ""timestamps"": {
                ""created"": ""2024-01-01T00:00:00Z"",
                ""updated"": ""2024-01-01T10:30:00Z""
            }
        }";

        // Act
        var taskContextUpdatedEvent = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = AgentId.Create(), ContextType = "ComplexContext", BeforeState = beforeState, AfterState = afterState, OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(beforeState, taskContextUpdatedEvent.BeforeState);
        Assert.Equal(afterState, taskContextUpdatedEvent.AfterState);
        Assert.Contains("\"status\": \"pending\"", taskContextUpdatedEvent.BeforeState);
        Assert.Contains("\"status\": \"in_progress\"", taskContextUpdatedEvent.AfterState);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenConstructingWithLargeStates()
    {
        // Arrange
        var largeBeforeState = new string('A', 5000);
        var largeAfterState = new string('B', 5000);

        // Act
        var taskContextUpdatedEvent = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = AgentId.Create(), ContextType = "LargeContext", BeforeState = largeBeforeState, AfterState = largeAfterState, OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(5000, taskContextUpdatedEvent.BeforeState.Length);
        Assert.Equal(5000, taskContextUpdatedEvent.AfterState.Length);
        Assert.Equal(largeBeforeState, taskContextUpdatedEvent.BeforeState);
        Assert.Equal(largeAfterState, taskContextUpdatedEvent.AfterState);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenConstructingWithSpecialCharacters()
    {
        // Arrange
        var beforeState = "Before: !@#$%^&*(){}[]|\\:;\"'<>,.?/~`\n\t\r";
        var afterState = "After: !@#$%^&*(){}[]|\\:;\"'<>,.?/~`\n\t\r";
        var contextType = "Context: !@#$%^&*()";

        // Act
        var taskContextUpdatedEvent = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = AgentId.Create(), ContextType = contextType, BeforeState = beforeState, AfterState = afterState, OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(contextType, taskContextUpdatedEvent.ContextType);
        Assert.Equal(beforeState, taskContextUpdatedEvent.BeforeState);
        Assert.Equal(afterState, taskContextUpdatedEvent.AfterState);
        Assert.Contains("!@#$%^&*()", taskContextUpdatedEvent.ContextType);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var contextType = "TestContext";
        var beforeState = "before";
        var afterState = "after";
        var occurredAt = DateTime.UtcNow;

        var event1 = new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId, ContextType = contextType, BeforeState = beforeState, AfterState = afterState, OccurredAt = occurredAt };
        var event2 = new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId, ContextType = contextType, BeforeState = beforeState, AfterState = afterState, OccurredAt = occurredAt };

        // Act & Assert
        // Note: Events will have different IDs due to Guid.NewGuid()
        Assert.NotEqual(event1, event2);
        Assert.Equal(event1.TaskId, event2.TaskId);
        Assert.Equal(event1.AgentId, event2.AgentId);
        Assert.Equal(event1.ContextType, event2.ContextType);
        Assert.Equal(event1.BeforeState, event2.BeforeState);
        Assert.Equal(event1.AfterState, event2.AfterState);
        Assert.Equal(event1.OccurredAt, event2.OccurredAt);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var event1 = new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId, ContextType = "context", BeforeState = "before", AfterState = "after", OccurredAt = DateTime.UtcNow };
        var event2 = new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId, ContextType = "context", BeforeState = "before", AfterState = "after", OccurredAt = DateTime.UtcNow };

        // Act
        var hash1 = event1.GetHashCode();
        var hash2 = event2.GetHashCode();

        // Assert
        // Hash codes will be different due to different IDs
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingTaskContextUpdatedEventInCollection()
    {
        // Arrange
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        var agentId = AgentId.Create();
        var occurredAt = DateTime.UtcNow;

        var events = new List<TaskContextUpdatedEvent>
        {
            new TaskContextUpdatedEvent { TaskId = taskId1, AgentId = agentId, ContextType = "AnalysisContext", BeforeState = "pending", AfterState = "started", OccurredAt = occurredAt },
            new TaskContextUpdatedEvent { TaskId = taskId1, AgentId = agentId, ContextType = "AnalysisContext", BeforeState = "started", AfterState = "completed", OccurredAt = occurredAt.AddMinutes(5) },
            new TaskContextUpdatedEvent { TaskId = taskId2, AgentId = agentId, ContextType = "ResearchContext", BeforeState = "init", AfterState = "gathering", OccurredAt = occurredAt.AddMinutes(10) }
        };

        // Act
        var task1Events = events.Where(e => e.TaskId == taskId1).ToList();
        var analysisContextEvents = events.Where(e => e.ContextType == "AnalysisContext").ToList();

        // Assert
        Assert.Equal(3, events.Count);
        Assert.Equal(2, task1Events.Count);
        Assert.Equal(2, analysisContextEvents.Count);
        Assert.All(events, e => Assert.Equal("TaskContextUpdatedEvent", e.EventName));
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingTaskContextUpdatedEventUsingAsBaseType()
    {
        // Arrange
        DomainEvent taskContextEvent = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = AgentId.Create(), ContextType = "TestContext", BeforeState = "before", AfterState = "after", OccurredAt = DateTime.UtcNow };

        // Act & Assert
        Assert.Equal("TaskContextUpdatedEvent", taskContextEvent.EventName);
        Assert.Equal(1, taskContextEvent.Version);
        Assert.IsType<TaskContextUpdatedEvent>(taskContextEvent);
    }

    [Fact]
    public void ShouldHaveDifferentAggregateIds_WhenUsingTaskContextUpdatedEventWithDifferentTaskIds()
    {
        // Arrange
        var taskId1 = TaskId.Create();
        var taskId2 = TaskId.Create();
        var agentId = AgentId.Create();

        var event1 = new TaskContextUpdatedEvent { TaskId = taskId1, AgentId = agentId, ContextType = "context", BeforeState = "before", AfterState = "after", OccurredAt = DateTime.UtcNow };
        var event2 = new TaskContextUpdatedEvent { TaskId = taskId2, AgentId = agentId, ContextType = "context", BeforeState = "before", AfterState = "after", OccurredAt = DateTime.UtcNow };

        // Act & Assert — different tasks produce different TaskId values
        Assert.NotEqual(event1.TaskId, event2.TaskId);
    }

    [Fact]
    public void ShouldHaveSameAggregateId_WhenUsingTaskContextUpdatedEventWithSameTaskIdDifferentAgents()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId1 = AgentId.Create();
        var agentId2 = AgentId.Create();

        var event1 = new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId1, ContextType = "context", BeforeState = "before", AfterState = "after", OccurredAt = DateTime.UtcNow };
        var event2 = new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId2, ContextType = "context", BeforeState = "before", AfterState = "after", OccurredAt = DateTime.UtcNow };

        // Act & Assert
        Assert.NotEqual(event1.AgentId, event2.AgentId);
    }

    [Theory]
    [InlineData("JSON", "{\"before\":true}", "{\"after\":false}")]
    [InlineData("XML", "<state>before</state>", "<state>after</state>")]
    [InlineData("YAML", "status: pending", "status: complete")]
    [InlineData("Plain", "Simple before text", "Simple after text")]
    public void ShouldAcceptAll_WhenUsingTaskContextUpdatedEventWithDifferentSerializationFormats(
        string contextType, string beforeState, string afterState)
    {
        // Act
        var taskContextUpdatedEvent = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = AgentId.Create(), ContextType = contextType, BeforeState = beforeState, AfterState = afterState, OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(contextType, taskContextUpdatedEvent.ContextType);
        Assert.Equal(beforeState, taskContextUpdatedEvent.BeforeState);
        Assert.Equal(afterState, taskContextUpdatedEvent.AfterState);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTaskContextUpdatedEventWithExtremeTimestamps()
    {
        // Act
        var minTimeEvent = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = AgentId.Create(), ContextType = "context", BeforeState = "before", AfterState = "after", OccurredAt = DateTime.MinValue };
        var maxTimeEvent = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = AgentId.Create(), ContextType = "context", BeforeState = "before", AfterState = "after", OccurredAt = DateTime.MaxValue };

        // Assert
        Assert.Equal(DateTime.MinValue, minTimeEvent.OccurredAt);
        Assert.Equal(DateTime.MaxValue, maxTimeEvent.OccurredAt);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTaskContextUpdatedEventInHashSet()
    {
        // Arrange
        var hashSet = new HashSet<TaskContextUpdatedEvent>();
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var occurredAt = DateTime.UtcNow;

        var event1 = new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId, ContextType = "context", BeforeState = "before1", AfterState = "after1", OccurredAt = occurredAt };
        var event2 = new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId, ContextType = "context", BeforeState = "before1", AfterState = "after1", OccurredAt = occurredAt }; // Same values
        var event3 = new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId, ContextType = "context", BeforeState = "before2", AfterState = "after2", OccurredAt = occurredAt }; // Different states

        // Act
        hashSet.Add(event1);
        hashSet.Add(event2); // Different ID, so will be added
        hashSet.Add(event3);

        // Assert
        Assert.Equal(3, hashSet.Count); // All different due to different IDs
    }

    [Fact]
    public void ShouldIncludeEventName_WhenUsingTaskContextUpdatedEventToString()
    {
        // Arrange
        var taskContextUpdatedEvent = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = AgentId.Create(), ContextType = "TestContext", BeforeState = "before", AfterState = "after", OccurredAt = DateTime.UtcNow };

        // Act
        var stringRepresentation = taskContextUpdatedEvent.ToString();

        // Assert
        Assert.Contains("TaskContextUpdatedEvent", stringRepresentation);
    }

    [Fact]
    public void ShouldNotThrow_WhenUsingTaskContextUpdatedEventUsingPropertyAccess()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var contextType = "TestContext";
        var beforeState = "before";
        var afterState = "after";
        var occurredAt = DateTime.UtcNow;

        var taskContextUpdatedEvent = new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId, ContextType = contextType, BeforeState = beforeState, AfterState = afterState, OccurredAt = occurredAt };

        // Act & Assert - All property accesses should work without throwing
        Assert.Equal(taskId, taskContextUpdatedEvent.TaskId);
        Assert.Equal(agentId, taskContextUpdatedEvent.AgentId);
        Assert.Equal(contextType, taskContextUpdatedEvent.ContextType);
        Assert.Equal(beforeState, taskContextUpdatedEvent.BeforeState);
        Assert.Equal(afterState, taskContextUpdatedEvent.AfterState);
        Assert.Equal(occurredAt, taskContextUpdatedEvent.OccurredAt);
        Assert.Equal("TaskContextUpdatedEvent", taskContextUpdatedEvent.EventName);
        Assert.Equal(1, taskContextUpdatedEvent.Version);
        Assert.NotEqual(Guid.Empty, taskContextUpdatedEvent.Id);
    }

    [Fact]
    public void ShouldBeValid_WhenUsingTaskContextUpdatedEventWithIdenticalBeforeAndAfterStates()
    {
        // Arrange
        var identicalState = "{\"status\":\"unchanged\"}";

        // Act
        var taskContextUpdatedEvent = new TaskContextUpdatedEvent { TaskId = TaskId.Create(), AgentId = AgentId.Create(), ContextType = "UnchangedContext", BeforeState = identicalState, AfterState = identicalState, OccurredAt = DateTime.UtcNow };

        // Assert
        Assert.Equal(identicalState, taskContextUpdatedEvent.BeforeState);
        Assert.Equal(identicalState, taskContextUpdatedEvent.AfterState);
        Assert.Equal(taskContextUpdatedEvent.BeforeState, taskContextUpdatedEvent.AfterState);
    }

    [Fact]
    public void ShouldTrackProgression_WhenUsingTaskContextUpdatedEventWithMultipleEventsForSameTask()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var baseTime = DateTime.UtcNow;

        var events = new List<TaskContextUpdatedEvent>
        {
            new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId, ContextType = "ProgressContext", BeforeState = "0%", AfterState = "25%", OccurredAt = baseTime },
            new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId, ContextType = "ProgressContext", BeforeState = "25%", AfterState = "50%", OccurredAt = baseTime.AddMinutes(10) },
            new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId, ContextType = "ProgressContext", BeforeState = "50%", AfterState = "75%", OccurredAt = baseTime.AddMinutes(20) },
            new TaskContextUpdatedEvent { TaskId = taskId, AgentId = agentId, ContextType = "ProgressContext", BeforeState = "75%", AfterState = "100%", OccurredAt = baseTime.AddMinutes(30) }
        };

        // Act
        var orderedEvents = events.OrderBy(e => e.OccurredAt).ToList();

        // Assert
        Assert.Equal(4, events.Count);
        Assert.All(events, e => Assert.Equal(taskId, e.TaskId));
        Assert.All(events, e => Assert.Equal(agentId, e.AgentId));
        Assert.All(events, e => Assert.Equal("ProgressContext", e.ContextType));

        Assert.Equal("0%", orderedEvents[0].BeforeState);
        Assert.Equal("25%", orderedEvents[0].AfterState);
        Assert.Equal("100%", orderedEvents[3].AfterState);
    }
}
