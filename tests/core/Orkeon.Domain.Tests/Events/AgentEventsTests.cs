using Orkeon.Domain.Common;
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Domain.Tests.Events;

public class AgentEventsTests
{
    private readonly AgentId _agentId = AgentId.From(Guid.NewGuid());
    private readonly TaskId _taskId = TaskId.From(Guid.NewGuid());
    private readonly DateTime _occurredAt = DateTime.UtcNow;

    [Fact]
    public void ShouldInitializeProperties_WhenUsingAgentCreatedEventUsingConstructor()
    {
        // Arrange
        var role = AgentRole.From(RoleDeveloper);
        var goal = AgentGoal.From("Build software");

        // Act
        var @event = new AgentCreatedEvent { AgentId = _agentId, Role = role, Goal = goal, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(role, @event.Role);
        Assert.Equal(goal, @event.Goal);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingAgentCreatedEventUsingEventName()
    {
        // Arrange
        var role = AgentRole.From(RoleDeveloper);
        var goal = AgentGoal.From("Build software");

        // Act
        var @event = new AgentCreatedEvent { AgentId = _agentId, Role = role, Goal = goal, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("AgentCreatedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingAgentAssignedToTaskEventUsingConstructor()
    {
        // Act
        var @event = new AgentAssignedToTaskEvent { AgentId = _agentId, TaskId = _taskId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingAgentStartedTaskEventUsingConstructor()
    {
        // Act
        var @event = new AgentStartedTaskEvent { AgentId = _agentId, TaskId = _taskId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingAgentCompletedTaskEventUsingConstructor()
    {
        // Arrange
        var output = TaskOutput.Create("Task completed successfully", "output", "pydantic");

        // Act
        var @event = new AgentCompletedTaskEvent { AgentId = _agentId, TaskId = _taskId, Output = output, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(output, @event.Output);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingAgentFailedTaskEventUsingConstructor()
    {
        // Arrange
        var reason = "Connection timeout";

        // Act
        var @event = new AgentFailedTaskEvent { AgentId = _agentId, TaskId = _taskId, Reason = reason, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(reason, @event.Reason);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingAgentCapabilitiesUpdatedEventUsingConstructor()
    {
        // Arrange
        var addedTools = new[] { "FileReader", "WebScraper" };
        var removedTools = new[] { "Calculator" };

        // Act
        var @event = new AgentCapabilitiesUpdatedEvent { AgentId = _agentId, AddedTools = addedTools, RemovedTools = removedTools, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(addedTools, @event.AddedTools);
        Assert.Equal(removedTools, @event.RemovedTools);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingAgentCapabilitiesUpdatedEventWithEmptyArrays()
    {
        // Arrange
        var addedTools = Array.Empty<string>();
        var removedTools = Array.Empty<string>();

        // Act
        var @event = new AgentCapabilitiesUpdatedEvent { AgentId = _agentId, AddedTools = addedTools, RemovedTools = removedTools, OccurredAt = _occurredAt };

        // Assert
        Assert.Empty(@event.AddedTools);
        Assert.Empty(@event.RemovedTools);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingAgentCollaborationStartedEventUsingConstructor()
    {
        // Arrange
        var collaboratorId = AgentId.From(Guid.NewGuid());
        var collaborationId = CollaborationId.From(Guid.NewGuid());

        // Act
        var @event = new AgentCollaborationStartedEvent { InitiatorId = _agentId, CollaboratorId = collaboratorId, TaskId = _taskId, CollaborationId = collaborationId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_agentId, @event.InitiatorId);
        Assert.Equal(collaboratorId, @event.CollaboratorId);
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(collaborationId, @event.CollaborationId);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingAgentMemoryUpdatedEventUsingConstructor()
    {
        // Arrange
        var memoryId = MemoryId.From(Guid.NewGuid());
        var memoryType = "ShortTerm";

        // Act
        var @event = new AgentMemoryUpdatedEvent { AgentId = _agentId, MemoryId = memoryId, MemoryType = memoryType, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(memoryId, @event.MemoryId);
        Assert.Equal(memoryType, @event.MemoryType);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldSupportEquality_WhenUsingAgentEventsAsRecords()
    {
        // Arrange
        var role = AgentRole.From(RoleDeveloper);
        var goal = AgentGoal.From("Build software");

        // Act
        var event1 = new AgentCreatedEvent { AgentId = _agentId, Role = role, Goal = goal, OccurredAt = _occurredAt };
        var event2 = new AgentCreatedEvent { AgentId = _agentId, Role = role, Goal = goal, OccurredAt = _occurredAt };

        // Assert
        // Records with different Ids should not be equal (since Id is generated)
        Assert.NotEqual(event1, event2);
        Assert.NotEqual(event1.Id, event2.Id);

        // But the data should be the same
        Assert.Equal(event1.AgentId, event2.AgentId);
        Assert.Equal(event1.Role, event2.Role);
        Assert.Equal(event1.Goal, event2.Goal);
        Assert.Equal(event1.OccurredAt, event2.OccurredAt);
    }

    [Fact]
    public void ShouldReturnOne_WhenUsingAgentEventsVersion()
    {
        // Arrange
        var role = AgentRole.From(RoleDeveloper);
        var goal = AgentGoal.From("Build software");

        // Act
        var @event = new AgentCreatedEvent { AgentId = _agentId, Role = role, Goal = goal, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(1, @event.Version);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingAgentStartedTaskEventUsingEventName()
    {
        // Act
        var @event = new AgentStartedTaskEvent { AgentId = _agentId, TaskId = _taskId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("AgentStartedTaskEvent", @event.EventName);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingAgentCompletedTaskEventUsingEventName()
    {
        // Arrange
        var output = TaskOutput.Create(Completed, "result", "json");

        // Act
        var @event = new AgentCompletedTaskEvent { AgentId = _agentId, TaskId = _taskId, Output = output, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("AgentCompletedTaskEvent", @event.EventName);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingAgentFailedTaskEventUsingEventName()
    {
        // Act
        var @event = new AgentFailedTaskEvent { AgentId = _agentId, TaskId = _taskId, Reason = "Error", OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("AgentFailedTaskEvent", @event.EventName);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingAgentCapabilitiesUpdatedEventUsingEventName()
    {
        // Act
        var @event = new AgentCapabilitiesUpdatedEvent { AgentId = _agentId, AddedTools = [], RemovedTools = [], OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("AgentCapabilitiesUpdatedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingAgentCollaborationStartedEventUsingEventName()
    {
        // Arrange
        var collaboratorId = AgentId.From(Guid.NewGuid());
        var collaborationId = CollaborationId.From(Guid.NewGuid());

        // Act
        var @event = new AgentCollaborationStartedEvent { InitiatorId = _agentId, CollaboratorId = collaboratorId, TaskId = _taskId, CollaborationId = collaborationId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("AgentCollaborationStartedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingAgentMemoryUpdatedEventUsingEventName()
    {
        // Arrange
        var memoryId = MemoryId.From(Guid.NewGuid());

        // Act
        var @event = new AgentMemoryUpdatedEvent { AgentId = _agentId, MemoryId = memoryId, MemoryType = "LongTerm", OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("AgentMemoryUpdatedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldHandleGracefully_WhenUsingAgentFailedTaskEventWithNullReason()
    {
        // Act
        var @event = new AgentFailedTaskEvent { AgentId = _agentId, TaskId = _taskId, Reason = null!, OccurredAt = _occurredAt };

        // Assert
        Assert.Null(@event.Reason);
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(_taskId, @event.TaskId);
    }

    [Fact]
    public void ShouldHandleGracefully_WhenUsingAgentMemoryUpdatedEventWithNullMemoryType()
    {
        // Arrange
        var memoryId = MemoryId.From(Guid.NewGuid());

        // Act
        var @event = new AgentMemoryUpdatedEvent { AgentId = _agentId, MemoryId = memoryId, MemoryType = null!, OccurredAt = _occurredAt };

        // Assert
        Assert.Null(@event.MemoryType);
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(memoryId, @event.MemoryId);
    }

    [Fact]
    public void ShouldHandleGracefully_WhenUsingAgentCapabilitiesUpdatedEventWithNullArrays()
    {
        // Act
        var @event = new AgentCapabilitiesUpdatedEvent { AgentId = _agentId, AddedTools = null!, RemovedTools = null!, OccurredAt = _occurredAt };

        // Assert
        Assert.Null(@event.AddedTools);
        Assert.Null(@event.RemovedTools);
        Assert.Equal(_agentId, @event.AgentId);
    }
}
