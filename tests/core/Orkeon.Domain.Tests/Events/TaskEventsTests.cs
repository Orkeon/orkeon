using Orkeon.Domain.Common;
using Orkeon.Domain.Task.Events;
using Orkeon.Domain.Task.ValueObjects;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Events;

public class TaskEventsTests
{
    private readonly TaskId _taskId = TaskId.From(Guid.NewGuid());
    private readonly AgentId _agentId = AgentId.From(Guid.NewGuid());
    private readonly DateTime _occurredAt = DateTime.UtcNow;

    [Fact]
    public void ShouldInitializeProperties_WhenUsingTaskCreatedEventUsingConstructor()
    {
        // Arrange
        var description = TaskDescription.From("Analyze customer data");

        // Act
        var @event = new TaskCreatedEvent { TaskId = _taskId, Description = description, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(description, @event.Description);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingTaskCreatedEventUsingEventName()
    {
        // Arrange
        var description = TaskDescription.From(GoalAnalyzeData);

        // Act
        var @event = new TaskCreatedEvent { TaskId = _taskId, Description = description, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("TaskCreatedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingTaskAssignedEventUsingConstructor()
    {
        // Act
        var @event = new TaskAssignedEvent { TaskId = _taskId, AgentId = _agentId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingTaskAssignedEventUsingEventName()
    {
        // Act
        var @event = new TaskAssignedEvent { TaskId = _taskId, AgentId = _agentId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("TaskAssignedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingTaskStatusChangedEventUsingConstructor()
    {
        // Arrange
        var oldStatus = TaskStatus.Pending;
        var newStatus = TaskStatus.InProgress;
        var reason = "Agent started processing";

        // Act
        var @event = new TaskStatusChangedEvent { TaskId = _taskId, OldStatus = oldStatus, NewStatus = newStatus, Reason = reason, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(oldStatus, @event.OldStatus);
        Assert.Equal(newStatus, @event.NewStatus);
        Assert.Equal(reason, @event.Reason);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingTaskStatusChangedEventWithNullReason()
    {
        // Arrange
        var oldStatus = TaskStatus.Pending;
        var newStatus = TaskStatus.InProgress;

        // Act
        var @event = new TaskStatusChangedEvent { TaskId = _taskId, OldStatus = oldStatus, NewStatus = newStatus, Reason = null, OccurredAt = _occurredAt };

        // Assert
        Assert.Null(@event.Reason);
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(oldStatus, @event.OldStatus);
        Assert.Equal(newStatus, @event.NewStatus);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingTaskStatusChangedEventUsingEventName()
    {
        // Act
        var @event = new TaskStatusChangedEvent { TaskId = _taskId, OldStatus = TaskStatus.Pending, NewStatus = TaskStatus.InProgress, Reason = null, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("TaskStatusChangedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingTaskStartedEventUsingConstructor()
    {
        // Act
        var @event = new TaskStartedEvent { TaskId = _taskId, AgentId = _agentId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingTaskStartedEventUsingEventName()
    {
        // Act
        var @event = new TaskStartedEvent { TaskId = _taskId, AgentId = _agentId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("TaskStartedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingTaskCompletedEventUsingConstructor()
    {
        // Arrange
        var output = TaskOutput.Create("Task completed successfully", "result", "json");
        var duration = TimeoutLong;

        // Act
        var @event = new TaskCompletedEvent { TaskId = _taskId, AgentId = _agentId, Output = output, Duration = duration, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(output, @event.Output);
        Assert.Equal(duration, @event.Duration);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingTaskCompletedEventUsingEventName()
    {
        // Arrange
        var output = TaskOutput.Create(Completed, "result", "json");

        // Act
        var @event = new TaskCompletedEvent { TaskId = _taskId, AgentId = _agentId, Output = output, Duration = TimeSpan.Zero, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("TaskCompletedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingTaskFailedEventUsingConstructor()
    {
        // Arrange
        var errorMessage = "Connection timeout";
        var exception = new TimeoutException("Timeout occurred");

        // Act
        var @event = new TaskFailedEvent { TaskId = _taskId, AgentId = _agentId, ErrorMessage = errorMessage, Exception = exception, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(errorMessage, @event.ErrorMessage);
        Assert.Equal(exception, @event.Exception);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingTaskFailedEventWithNullAgentId()
    {
        // Arrange
        var errorMessage = "Task could not be assigned";

        // Act
        var @event = new TaskFailedEvent { TaskId = _taskId, AgentId = null, ErrorMessage = errorMessage, Exception = null, OccurredAt = _occurredAt };

        // Assert
        Assert.Null(@event.AgentId);
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(errorMessage, @event.ErrorMessage);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingTaskFailedEventWithNullException()
    {
        // Arrange
        var errorMessage = "Unknown error";

        // Act
        var @event = new TaskFailedEvent { TaskId = _taskId, AgentId = _agentId, ErrorMessage = errorMessage, Exception = null, OccurredAt = _occurredAt };

        // Assert
        Assert.Null(@event.Exception);
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(_agentId, @event.AgentId);
        Assert.Equal(errorMessage, @event.ErrorMessage);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingTaskFailedEventUsingEventName()
    {
        // Act
        var @event = new TaskFailedEvent { TaskId = _taskId, AgentId = null, ErrorMessage = "Error", Exception = null, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("TaskFailedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingTaskCancelledEventUsingConstructor()
    {
        // Arrange
        var reason = "User cancelled operation";

        // Act
        var @event = new TaskCancelledEvent { TaskId = _taskId, Reason = reason, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(reason, @event.Reason);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingTaskCancelledEventUsingEventName()
    {
        // Act
        var @event = new TaskCancelledEvent { TaskId = _taskId, Reason = "Cancelled", OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("TaskCancelledEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingTaskDependenciesUpdatedEventUsingConstructor()
    {
        // Arrange
        var addedDeps = new[] { TaskId.From(Guid.NewGuid()), TaskId.From(Guid.NewGuid()) };
        var removedDeps = new[] { TaskId.From(Guid.NewGuid()) };

        // Act
        var @event = new TaskDependenciesUpdatedEvent { TaskId = _taskId, AddedDependencies = addedDeps, RemovedDependencies = removedDeps, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(addedDeps, @event.AddedDependencies);
        Assert.Equal(removedDeps, @event.RemovedDependencies);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingTaskDependenciesUpdatedEventWithEmptyArrays()
    {
        // Arrange
        var addedDeps = Array.Empty<TaskId>();
        var removedDeps = Array.Empty<TaskId>();

        // Act
        var @event = new TaskDependenciesUpdatedEvent { TaskId = _taskId, AddedDependencies = addedDeps, RemovedDependencies = removedDeps, OccurredAt = _occurredAt };

        // Assert
        Assert.Empty(@event.AddedDependencies);
        Assert.Empty(@event.RemovedDependencies);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingTaskDependenciesUpdatedEventUsingEventName()
    {
        // Act
        var @event = new TaskDependenciesUpdatedEvent { TaskId = _taskId, AddedDependencies = [], RemovedDependencies = [], OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("TaskDependenciesUpdatedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingTaskBlockedEventUsingConstructor()
    {
        // Arrange
        var blockingTasks = new[] { TaskId.From(Guid.NewGuid()), TaskId.From(Guid.NewGuid()) };

        // Act
        var @event = new TaskBlockedEvent { TaskId = _taskId, BlockingTasks = blockingTasks, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(blockingTasks, @event.BlockingTasks);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingTaskBlockedEventWithEmptyBlockingTasks()
    {
        // Arrange
        var blockingTasks = Array.Empty<TaskId>();

        // Act
        var @event = new TaskBlockedEvent { TaskId = _taskId, BlockingTasks = blockingTasks, OccurredAt = _occurredAt };

        // Assert
        Assert.Empty(@event.BlockingTasks);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingTaskBlockedEventUsingEventName()
    {
        // Act
        var @event = new TaskBlockedEvent { TaskId = _taskId, BlockingTasks = [], OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("TaskBlockedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldInitializeProperties_WhenUsingTaskUnblockedEventUsingConstructor()
    {
        // Act
        var @event = new TaskUnblockedEvent { TaskId = _taskId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(_occurredAt, @event.OccurredAt);
        Assert.NotEqual(Guid.Empty, @event.Id);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingTaskUnblockedEventUsingEventName()
    {
        // Act
        var @event = new TaskUnblockedEvent { TaskId = _taskId, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal("TaskUnblockedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldSupportEquality_WhenUsingTaskEventsAsRecords()
    {
        // Arrange
        var description = TaskDescription.From("Process data");

        // Act
        var event1 = new TaskCreatedEvent { TaskId = _taskId, Description = description, OccurredAt = _occurredAt };
        var event2 = new TaskCreatedEvent { TaskId = _taskId, Description = description, OccurredAt = _occurredAt };

        // Assert
        // Records with different Ids should not be equal (since Id is generated)
        Assert.NotEqual(event1, event2);
        Assert.NotEqual(event1.Id, event2.Id);

        // But the data should be the same
        Assert.Equal(event1.TaskId, event2.TaskId);
        Assert.Equal(event1.Description, event2.Description);
        Assert.Equal(event1.OccurredAt, event2.OccurredAt);
    }

    [Fact]
    public void ShouldReturnOne_WhenUsingTaskEventsVersion()
    {
        // Arrange
        var description = TaskDescription.From("Test task");

        // Act
        var @event = new TaskCreatedEvent { TaskId = _taskId, Description = description, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(1, @event.Version);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingTaskCompletedEventWithZeroDuration()
    {
        // Arrange
        var output = TaskOutput.Create("Instant completion", "result", "json");

        // Act
        var @event = new TaskCompletedEvent { TaskId = _taskId, AgentId = _agentId, Output = output, Duration = TimeSpan.Zero, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(TimeSpan.Zero, @event.Duration);
        Assert.Equal(output, @event.Output);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingTaskCompletedEventWithLargeDuration()
    {
        // Arrange
        var output = TaskOutput.Create("Long running task", "result", "json");
        var duration = TimeSpan.FromDays(30);

        // Act
        var @event = new TaskCompletedEvent { TaskId = _taskId, AgentId = _agentId, Output = output, Duration = duration, OccurredAt = _occurredAt };

        // Assert
        Assert.Equal(duration, @event.Duration);
        Assert.Equal(output, @event.Output);
    }

    [Fact]
    public void ShouldHandleGracefully_WhenUsingTaskCancelledEventWithNullReason()
    {
        // Act
        var @event = new TaskCancelledEvent { TaskId = _taskId, Reason = null!, OccurredAt = _occurredAt };

        // Assert
        Assert.Null(@event.Reason);
        Assert.Equal(_taskId, @event.TaskId);
    }

    [Fact]
    public void ShouldHandleGracefully_WhenUsingTaskDependenciesUpdatedEventWithNullArrays()
    {
        // Act
        var @event = new TaskDependenciesUpdatedEvent { TaskId = _taskId, AddedDependencies = null!, RemovedDependencies = null!, OccurredAt = _occurredAt };

        // Assert
        Assert.Null(@event.AddedDependencies);
        Assert.Null(@event.RemovedDependencies);
        Assert.Equal(_taskId, @event.TaskId);
    }

    [Fact]
    public void ShouldHandleGracefully_WhenUsingTaskBlockedEventWithNullBlockingTasks()
    {
        // Act
        var @event = new TaskBlockedEvent { TaskId = _taskId, BlockingTasks = null!, OccurredAt = _occurredAt };

        // Assert
        Assert.Null(@event.BlockingTasks);
        Assert.Equal(_taskId, @event.TaskId);
    }

    [Fact]
    public void ShouldHandleGracefully_WhenUsingTaskFailedEventWithNullErrorMessage()
    {
        // Act
        var @event = new TaskFailedEvent { TaskId = _taskId, AgentId = _agentId, ErrorMessage = null!, Exception = null, OccurredAt = _occurredAt };

        // Assert
        Assert.Null(@event.ErrorMessage);
        Assert.Equal(_taskId, @event.TaskId);
        Assert.Equal(_agentId, @event.AgentId);
    }
}
