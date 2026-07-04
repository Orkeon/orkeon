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
    public void ShouldInitializeProperties_WhenUsingAgentLeftCrewEventUsingConstructor()
    {
        // Arrange
        var reason = "Task completed";

        // Act
        var @event = new AgentLeftCrewEvent { CrewId = _crewId, AgentId = _agentId, Reason = reason, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(reason, @event.Reason);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingAgentLeftCrewEventUsingEventName()
    {
        // Act
        var @event = new AgentLeftCrewEvent { CrewId = _crewId, AgentId = _agentId, Reason = "Reason", OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("AgentLeftCrewEvent", @event.EventName);
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
    public void ShouldInitializeProperties_WhenUsingTaskRemovedFromCrewEventUsingConstructor()
    {
        // Arrange
        var reason = "No longer needed";

        // Act
        var @event = new TaskRemovedFromCrewEvent { CrewId = _crewId, TaskId = _taskId, Reason = reason, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(reason, @event.Reason);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingTaskRemovedFromCrewEventUsingEventName()
    {
        // Act
        var @event = new TaskRemovedFromCrewEvent { CrewId = _crewId, TaskId = _taskId, Reason = "Reason", OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("TaskRemovedFromCrewEvent", @event.EventName);
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
        var failedTasks = 2;

        // Act
        var @event = new CrewExecutionCompletedEvent { CrewId = _crewId, ProcessId = _processId, Duration = duration, CompletedTasks = completedTasks, FailedTasks = failedTasks, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(_processId, @event.ProcessId);
        Assert.Equal(duration, @event.Duration);
        Assert.Equal(completedTasks, @event.CompletedTasks);
        Assert.Equal(failedTasks, @event.FailedTasks);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingCrewExecutionCompletedEventUsingEventName()
    {
        // Act
        var @event = new CrewExecutionCompletedEvent { CrewId = _crewId, ProcessId = _processId, Duration = TimeSpan.Zero, CompletedTasks = 0, FailedTasks = 0, OccurredAt = _occurredAt };

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
    public void ShouldInitializeProperties_WhenUsingCrewProcessTypeChangedEventUsingConstructor()
    {
        // Arrange
        var oldProcessType = ProcessType.Sequential;
        var newProcessType = ProcessType.Parallel;

        // Act
        var @event = new CrewProcessTypeChangedEvent { CrewId = _crewId, OldProcessType = oldProcessType, NewProcessType = newProcessType, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(oldProcessType, @event.OldProcessType);
        Assert.Equal(newProcessType, @event.NewProcessType);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingCrewProcessTypeChangedEventUsingEventName()
    {
        // Act
        var @event = new CrewProcessTypeChangedEvent { CrewId = _crewId, OldProcessType = ProcessType.Sequential, NewProcessType = ProcessType.Parallel, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("CrewProcessTypeChangedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingCrewGoalUpdatedEventUsingConstructor()
    {
        // Arrange
        var oldGoal = "Process customer data";
        var newGoal = "Analyze customer behavior";

        // Act
        var @event = new CrewGoalUpdatedEvent { CrewId = _crewId, OldGoal = oldGoal, NewGoal = newGoal, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(oldGoal, @event.OldGoal);
        Assert.Equal(newGoal, @event.NewGoal);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingCrewGoalUpdatedEventUsingEventName()
    {
        // Act
        var @event = new CrewGoalUpdatedEvent { CrewId = _crewId, OldGoal = "Old", NewGoal = "New", OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("CrewGoalUpdatedEvent", @event.EventName);
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
        var @event = new CrewExecutionCompletedEvent { CrewId = _crewId, ProcessId = _processId, Duration = TimeSpan.Zero, CompletedTasks = 0, FailedTasks = 0, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(TimeSpan.Zero, @event.Duration);
        Assert.Equal(0, @event.CompletedTasks);
        Assert.Equal(0, @event.FailedTasks);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingCrewExecutionCompletedEventWithLargeDuration()
    {
        // Arrange
        var duration = TimeSpan.FromDays(7);
        var completedTasks = 1000;
        var failedTasks = 50;

        // Act
        var @event = new CrewExecutionCompletedEvent { CrewId = _crewId, ProcessId = _processId, Duration = duration, CompletedTasks = completedTasks, FailedTasks = failedTasks, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(duration, @event.Duration);
        Assert.Equal(completedTasks, @event.CompletedTasks);
        Assert.Equal(failedTasks, @event.FailedTasks);
    }

    [Fact]
    public void ShouldHandleGracefully_WhenUsingAgentLeftCrewEventWithNullReason()
    {
        // Act
        var @event = new AgentLeftCrewEvent { CrewId = _crewId, AgentId = _agentId, Reason = null!, OccurredAt = _occurredAt };

        // Assert
        Assert.Null(@event.Reason);
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(_agentId, @event.AgentId);
    }

    [Fact]
    public void ShouldHandleGracefully_WhenUsingTaskRemovedFromCrewEventWithNullReason()
    {
        // Act
        var @event = new TaskRemovedFromCrewEvent { CrewId = _crewId, TaskId = _taskId, Reason = null!, OccurredAt = _occurredAt };

        // Assert
        Assert.Null(@event.Reason);
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(_taskId, @event.TaskId);
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

    [Fact]
    public void ShouldHandleGracefully_WhenUsingCrewGoalUpdatedEventWithNullGoals()
    {
        // Act
        var @event = new CrewGoalUpdatedEvent { CrewId = _crewId, OldGoal = null!, NewGoal = null!, OccurredAt = _occurredAt };

        // Assert
        Assert.Null(@event.OldGoal);
        Assert.Null(@event.NewGoal);
        Assert.Equal(_crewId, @event.CrewId);
    }
}
