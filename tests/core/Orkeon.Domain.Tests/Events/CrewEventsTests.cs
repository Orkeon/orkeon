using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.Events;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.Events;

public class CrewEventsTests
{
    private readonly CrewId _crewId = CrewId.From(Guid.NewGuid());
    private readonly AgentId _agentId = AgentId.From(Guid.NewGuid());
    private readonly TaskId _taskId = TaskId.From(Guid.NewGuid());
    private readonly ProcessId _processId = ProcessId.From(Guid.NewGuid());
    private readonly DateTime _occurredAt = DateTime.UtcNow;

    [Fact]
    public void ShouldInitializeProperties_WhenUsingCrewCreatedEventUsingConstructor()
    {
        // Arrange
        var goal = "Process customer data";
        var processType = ProcessType.Sequential;

        // Act
        var @event = new CrewCreatedEvent { CrewId = _crewId, Goal = goal, ProcessType = processType, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(goal, @event.Goal);
        Assert.Equal(processType, @event.ProcessType);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingCrewCreatedEventUsingEventName()
    {
        // Act
        var @event = new CrewCreatedEvent { CrewId = _crewId, Goal = "Goal", ProcessType = ProcessType.Sequential, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("CrewCreatedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingAgentJoinedCrewEventUsingConstructor()
    {
        // Act
        var @event = new AgentJoinedCrewEvent { CrewId = _crewId, AgentId = _agentId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingAgentJoinedCrewEventUsingEventName()
    {
        // Act
        var @event = new AgentJoinedCrewEvent { CrewId = _crewId, AgentId = _agentId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("AgentJoinedCrewEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingTaskAddedToCrewEventUsingConstructor()
    {
        // Act
        var @event = new TaskAddedToCrewEvent { CrewId = _crewId, TaskId = _taskId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingTaskAddedToCrewEventUsingEventName()
    {
        // Act
        var @event = new TaskAddedToCrewEvent { CrewId = _crewId, TaskId = _taskId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("TaskAddedToCrewEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingCrewExecutionStartedEventUsingConstructor()
    {
        // Act
        var @event = new CrewExecutionStartedEvent { CrewId = _crewId, ProcessId = _processId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(_processId, @event.ProcessId);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingCrewExecutionStartedEventUsingEventName()
    {
        // Act
        var @event = new CrewExecutionStartedEvent { CrewId = _crewId, ProcessId = _processId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("CrewExecutionStartedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingCrewExecutionCompletedEventUsingConstructor()
    {
        // Arrange
        var duration = TimeSpan.FromMinutes(30);
        var completedTasks = 10;

        // Act
        var @event = new CrewExecutionCompletedEvent { CrewId = _crewId, ProcessId = _processId, Duration = duration, CompletedTasks = completedTasks, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(_processId, @event.ProcessId);
        Assert.Equal(duration, @event.Duration);
        Assert.Equal(completedTasks, @event.CompletedTasks);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingCrewExecutionCompletedEventUsingEventName()
    {
        // Act
        var @event = new CrewExecutionCompletedEvent { CrewId = _crewId, ProcessId = _processId, Duration = TimeSpan.Zero, CompletedTasks = 0, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("CrewExecutionCompletedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingCrewExecutionFailedEventUsingConstructor()
    {
        // Arrange
        var reason = "Connection timeout";
        var exception = new TimeoutException("Timeout occurred");

        // Act
        var @event = new CrewExecutionFailedEvent { CrewId = _crewId, ProcessId = _processId, Reason = reason, Exception = exception, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(_processId, @event.ProcessId);
        Assert.Equal(reason, @event.Reason);
        Assert.Equal(exception, @event.Exception);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingCrewExecutionFailedEventWithNullException()
    {
        // Arrange
        var reason = "Unknown error";

        // Act
        var @event = new CrewExecutionFailedEvent { CrewId = _crewId, ProcessId = _processId, Reason = reason, Exception = null, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(_processId, @event.ProcessId);
        Assert.Equal(reason, @event.Reason);
        Assert.Null(@event.Exception);
        Assert.Equal(_occurredAt, @event.OccurredAt);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingCrewExecutionFailedEventUsingEventName()
    {
        // Act
        var @event = new CrewExecutionFailedEvent { CrewId = _crewId, ProcessId = _processId, Reason = "Reason", Exception = null, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("CrewExecutionFailedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldSupportEquality_WhenUsingCrewEventsAsRecords()
    {
        // Arrange
        var goal = "Process data";
        var processType = ProcessType.Sequential;

        // Act
        var event1 = new CrewCreatedEvent { CrewId = _crewId, Goal = goal, ProcessType = processType, OccurredAt = _occurredAt };
        var event2 = new CrewCreatedEvent { CrewId = _crewId, Goal = goal, ProcessType = processType, OccurredAt = _occurredAt };

        // Assert
        // Records with different Ids should not be equal (since Id is generated)
        Assert.NotEqual(event1, event2);
        Assert.NotEqual(event1.Id, event2.Id);

        // But the data should be the same
        Assert.Equal(event1.CrewId, event2.CrewId);
        Assert.Equal(event1.Goal, event2.Goal);
        Assert.Equal(event1.ProcessType, event2.ProcessType);
        Assert.Equal(event1.OccurredAt, event2.OccurredAt);
    }

    [Fact]
    public void ShouldReturnOne_WhenUsingCrewEventsVersion()
    {
        // Act
        var @event = new CrewCreatedEvent { CrewId = _crewId, Goal = "Goal", ProcessType = ProcessType.Sequential, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(1, @event.Version);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingCrewExecutionCompletedEventWithZeroDuration()
    {
        // Act
        var @event = new CrewExecutionCompletedEvent { CrewId = _crewId, ProcessId = _processId, Duration = TimeSpan.Zero, CompletedTasks = 0, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(TimeSpan.Zero, @event.Duration);
        Assert.Equal(0, @event.CompletedTasks);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingCrewExecutionCompletedEventWithLargeDuration()
    {
        // Arrange
        var duration = TimeSpan.FromDays(7);
        var completedTasks = 1000;

        // Act
        var @event = new CrewExecutionCompletedEvent { CrewId = _crewId, ProcessId = _processId, Duration = duration, CompletedTasks = completedTasks, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(duration, @event.Duration);
        Assert.Equal(completedTasks, @event.CompletedTasks);
    }

    [Fact]
    public void ShouldHandleGracefully_WhenUsingCrewExecutionFailedEventWithNullReason()
    {
        // Act
        var @event = new CrewExecutionFailedEvent { CrewId = _crewId, ProcessId = _processId, Reason = null!, Exception = null, OccurredAt = _occurredAt };

        // Assert
        Assert.Null(@event.Reason);
        Assert.Null(@event.Exception);
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(_processId, @event.ProcessId);
    }

}
