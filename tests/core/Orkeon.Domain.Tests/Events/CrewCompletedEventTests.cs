using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Crew.Events;
using Orkeon.Domain.Crew.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Events;

public class CrewCompletedEventTests
{
    private readonly CrewId _crewId = CrewId.From(Guid.NewGuid());
    private readonly DateTime _startTime = DateTime.UtcNow.AddMinutes(-10);
    private readonly DateTime _endTime = DateTime.UtcNow;

    [Fact]
    public void ShouldInitializeProperties_WhenUsingCrewCompletedEventUsingConstructor()
    {
        // Arrange
        var taskOutputs = new[]
        {
            TaskOutput.Create("Task 1 completed", "result1", "json"),
            TaskOutput.Create("Task 2 completed", "result2", "json")
        };
        var crewOutput = new CrewOutput(
            "Crew execution completed",
            null,
            taskOutputs,
            true,
            _endTime - _startTime);

        // Act
        var @event = CrewCompletedEvent.FromOutput(_crewId, crewOutput);

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal("Crew execution completed", @event.Result.Output);
        Assert.True(@event.Result.Success);
        Assert.Equal(2, @event.Result.TaskCount);
        Assert.NotEqual(Guid.Empty, @event.Id);
        Assert.True(@event.OccurredAt <= DateTime.UtcNow);
        Assert.True(@event.OccurredAt >= DateTime.UtcNow.AddSeconds(-1));
    }

    [Fact]
    public void ShouldThrowException_WhenUsingCrewCompletedEventUsingConstructorWithNullCrewId()
    {
        // Arrange
        var taskOutputs = new[] { TaskOutput.Create(Completed, "result", "json") };
        var crewOutput = new CrewOutput(
            "Crew execution completed",
            null,
            taskOutputs,
            true,
            TimeoutStandard);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            CrewCompletedEvent.FromOutput(null!, crewOutput));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingCrewCompletedEventUsingConstructorWithNullOutput()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            CrewCompletedEvent.FromOutput(_crewId, (CrewOutput)null!));
        Assert.Equal("output", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingCrewCompletedEventUsingEventName()
    {
        // Arrange
        var result = CrewCompletionResult.Create("Crew execution completed", true, TimeoutStandard, 1, 1);

        // Act
        var @event = new CrewCompletedEvent { CrewId = _crewId, Result = result };

        // Assert
        Assert.Equal("CrewCompletedEvent", @event.EventName);
    }

    [Fact]
    public void ShouldReturnOne_WhenUsingCrewCompletedEventUsingVersion()
    {
        // Arrange
        var result = CrewCompletionResult.Create("Crew execution completed", true, TimeoutStandard, 1, 1);

        // Act
        var @event = new CrewCompletedEvent { CrewId = _crewId, Result = result };

        // Assert
        Assert.Equal(1, @event.Version);
    }

    [Fact]
    public void ShouldBeImmutable_WhenUsingCrewCompletedEvent()
    {
        // Arrange
        var result = CrewCompletionResult.Create("Crew execution completed", true, TimeoutStandard, 1, 1);
        var @event = new CrewCompletedEvent { CrewId = _crewId, Result = result };

        // Act - Try to create a new instance with modified properties
        var newCrewId = CrewId.From(Guid.NewGuid());
        var newResult = CrewCompletionResult.Create("Different crew execution", true, TimeoutExtended, 1, 1);

        var modifiedEvent = @event with
        {
            CrewId = newCrewId,
            Result = newResult
        };

        // Assert - Original event should remain unchanged
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(result, @event.Result);

        // Modified event should have new values
        Assert.Equal(newCrewId, modifiedEvent.CrewId);
        Assert.Equal(newResult, modifiedEvent.Result);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingCrewCompletedEventWithEmptyTaskOutputs()
    {
        // Arrange
        var emptyTaskOutputs = Array.Empty<TaskOutput>();
        var crewOutput = new CrewOutput(
            "Crew execution completed",
            null,
            emptyTaskOutputs,
            true,
            TimeSpan.Zero);

        // Act
        var @event = CrewCompletedEvent.FromOutput(_crewId, crewOutput);

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(0, @event.Result.TaskCount);
        Assert.Equal(TimeSpan.Zero, @event.Result.ExecutionTime);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenUsingCrewCompletedEventWithLargeNumberOfTaskOutputs()
    {
        // Arrange
        var taskOutputs = new TaskOutput[100];
        for (int i = 0; i < 100; i++)
        {
            taskOutputs[i] = TaskOutput.Create($"Task {i} completed", $"result{i}", "json");
        }
        var crewOutput = new CrewOutput(
            "Crew execution completed with 100 tasks",
            null,
            taskOutputs,
            true,
            TimeSpan.FromHours(2));

        // Act
        var @event = CrewCompletedEvent.FromOutput(_crewId, crewOutput);

        // Assert
        Assert.Equal(_crewId, @event.CrewId);
        Assert.Equal(100, @event.Result.TaskCount);
        Assert.Equal(TimeSpan.FromHours(2), @event.Result.ExecutionTime);
    }

    [Fact]
    public void ShouldInheritFromDomainEvent_WhenUsingCrewCompletedEvent()
    {
        // Arrange
        var result = CrewCompletionResult.Create("Crew execution completed", true, TimeoutStandard, 1, 1);

        // Act
        var @event = new CrewCompletedEvent { CrewId = _crewId, Result = result };

        // Assert
        Assert.IsType<CrewCompletedEvent>(@event);
    }

    [Fact]
    public void ShouldSupportValueComparison_WhenUsingCrewCompletedEventUsingAsRecord()
    {
        // Arrange
        var result = CrewCompletionResult.Create("Crew execution completed", true, TimeoutStandard, 1, 1);

        // Act
        var event1 = new CrewCompletedEvent { CrewId = _crewId, Result = result };
        var event2 = new CrewCompletedEvent { CrewId = _crewId, Result = result };

        // Assert
        // Different instances with different Ids
        Assert.NotEqual(event1.Id, event2.Id);
        Assert.NotEqual(event1, event2);

        // But same data
        Assert.Equal(event1.CrewId, event2.CrewId);
        Assert.Equal(event1.Result, event2.Result);
    }

    [Fact]
    public void ShouldBeSetToCurrentUtcTime_WhenUsingCrewCompletedEventUsingOccurredAt()
    {
        // Arrange
        var result = CrewCompletionResult.Create("Crew execution completed", true, TimeoutStandard, 1, 1);
        var beforeCreation = DateTime.UtcNow;

        // Act
        var @event = new CrewCompletedEvent { CrewId = _crewId, Result = result };
        var afterCreation = DateTime.UtcNow;

        // Assert
        Assert.True(@event.OccurredAt >= beforeCreation);
        Assert.True(@event.OccurredAt <= afterCreation);
    }

    [Fact]
    public void ShouldBeUnique_WhenUsingCrewCompletedEventUsingId()
    {
        // Arrange
        var result = CrewCompletionResult.Create("Crew execution completed", true, TimeoutStandard, 1, 1);

        // Act
        var event1 = new CrewCompletedEvent { CrewId = _crewId, Result = result };
        var event2 = new CrewCompletedEvent { CrewId = _crewId, Result = result };

        // Assert
        Assert.NotEqual(event1.Id, event2.Id);
        Assert.NotEqual(Guid.Empty, event1.Id);
        Assert.NotEqual(Guid.Empty, event2.Id);
    }

    [Fact]
    public void ShouldCreateFromCrewOutput_WhenUsingCrewCompletedEventWithCrewOutputConstructor()
    {
        // Arrange
        var taskOutputs = new[]
        {
            TaskOutput.Create("Task 1 completed", "result1", "json"),
            TaskOutput.Create("Task 2 failed", "result2", "json", success: false)
        };
        var crewOutput = new CrewOutput(
            "Crew execution completed",
            null,
            taskOutputs,
            true,
            TimeoutStandard);

        // Act
        var @event = CrewCompletedEvent.FromOutput(_crewId, crewOutput);

        // Assert
        Assert.Equal("Crew execution completed", @event.Result.Output);
        Assert.True(@event.Result.Success);
        Assert.Equal(TimeoutStandard, @event.Result.ExecutionTime);
        Assert.Equal(2, @event.Result.TaskCount);
        Assert.Equal(1, @event.Result.SuccessfulTaskCount);
        Assert.Null(@event.Result.Error);
    }

    [Fact]
    public void ShouldCreateDirectlyFromResult_WhenUsingCrewCompletionResult()
    {
        // Arrange
        var result = CrewCompletionResult.Create(
            "Direct result",
            success: false,
            executionTime: TimeSpan.FromMinutes(3),
            taskCount: 5,
            successfulTaskCount: 3,
            error: "Partial failure");

        // Act
        var @event = new CrewCompletedEvent { CrewId = _crewId, Result = result };

        // Assert
        Assert.Equal("Direct result", @event.Result.Output);
        Assert.False(@event.Result.Success);
        Assert.Equal("Partial failure", @event.Result.Error);
        Assert.Equal(5, @event.Result.TaskCount);
        Assert.Equal(3, @event.Result.SuccessfulTaskCount);
    }
}
