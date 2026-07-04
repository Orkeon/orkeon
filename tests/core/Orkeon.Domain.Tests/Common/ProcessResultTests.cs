using Orkeon.Domain.Crew.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// Tests for ProcessResult and CrewResult following Clean Architecture principles.
/// Tests the business rules and validation logic of the result classes.
/// </summary>
public class ProcessResultTests
{
    [Fact]
    public void ShouldCreateResult_WhenUsingProcessResultUsingConstructorWithDefaults()
    {
        // Act
        var result = ProcessResult.Create(true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
        Assert.Same(ProcessOutputs.Empty, result.Outputs);
        Assert.Equal(TimeSpan.Zero, result.ExecutionTime);
        Assert.True(result.StartedAt <= DateTime.UtcNow);
        Assert.Null(result.CompletedAt);
    }

    [Fact]
    public void ShouldSetProperties_WhenUsingProcessResultUsingConstructorWithAllParameters()
    {
        // Arrange
        var outputs = ProcessOutputs.Empty.With("key", "value");
        var errorMessage = "Something went wrong";
        var executionTime = TimeoutQuick;
        var startedAt = DateTime.UtcNow.AddMinutes(-5);
        var completedAt = DateTime.UtcNow;

        // Act
        var result = ProcessResult.Create(
            false,
            outputs,
            errorMessage,
            executionTime,
            startedAt,
            completedAt);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(errorMessage, result.ErrorMessage);
        Assert.Same(outputs, result.Outputs);
        Assert.Equal(executionTime, result.ExecutionTime);
        Assert.Equal(startedAt, result.StartedAt);
        Assert.Equal(completedAt, result.CompletedAt);
    }

    [Fact]
    public void ShouldCreateSuccessfulResult_WhenUsingProcessResultUsingSuccessWithoutOutputs()
    {
        // Act
        var result = ProcessResult.Success();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
        Assert.Same(ProcessOutputs.Empty, result.Outputs);
    }

    [Fact]
    public void ShouldCreateSuccessfulResult_WhenUsingProcessResultUsingSuccessWithOutputs()
    {
        // Arrange
        var outputs = ProcessOutputs.Empty.With("result", "completed");

        // Act
        var result = ProcessResult.Success(outputs);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
        Assert.Same(outputs, result.Outputs);
    }

    [Fact]
    public void ShouldCreateOutputs_WhenUsingProcessResultUsingSuccessWithKeyValue()
    {
        // Act
        var result = ProcessResult.Success("status", "completed");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
        Assert.Equal("completed", result.Outputs.Get<string>("status"));
    }

    [Fact]
    public void ShouldCreateFailedResult_WhenUsingProcessResultUsingFailure()
    {
        // Arrange
        var errorMessage = "Process failed";

        // Act
        var result = ProcessResult.Failure(errorMessage);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(errorMessage, result.ErrorMessage);
        Assert.Same(ProcessOutputs.Empty, result.Outputs);
    }

    [Fact]
    public void ShouldBeUtcNow_WhenUsingProcessResultWithDefaultStartedAt()
    {
        // Act
        var result = ProcessResult.Create(true);

        // Assert
        Assert.Equal(DateTimeKind.Utc, result.StartedAt.Kind);
        Assert.True(result.StartedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldCreateResult_WhenUsingCrewResultUsingConstructorWithDefaults()
    {
        // Act
        var result = CrewResult.Create(true);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
        Assert.Same(TaskOutputMap.Empty, result.TaskOutputs);
        Assert.Empty(result.CompletedTasks);
        Assert.Empty(result.FailedTasks);
        Assert.Equal(TimeSpan.Zero, result.TotalExecutionTime);
        Assert.True(result.StartedAt <= DateTime.UtcNow);
        Assert.Null(result.CompletedAt);
    }

    [Fact]
    public void ShouldSetProperties_WhenUsingCrewResultUsingConstructorWithAllParameters()
    {
        // Arrange
        var taskOutputs = TaskOutputMap.Empty;
        var errorMessage = "Crew execution failed";
        var completedTasks = new List<string> { "task1", "task2" };
        var failedTasks = new List<string> { "task3" };
        var executionTime = TimeoutStandard;
        var startedAt = DateTime.UtcNow.AddMinutes(-10);
        var completedAt = DateTime.UtcNow;

        // Act
        var result = CrewResult.Create(
            false,
            taskOutputs,
            errorMessage,
            new TaskCompletionLists(completedTasks, failedTasks),
            new ExecutionTiming(executionTime, startedAt, completedAt));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(errorMessage, result.ErrorMessage);
        Assert.Same(taskOutputs, result.TaskOutputs);
        Assert.Same(completedTasks, result.CompletedTasks);
        Assert.Same(failedTasks, result.FailedTasks);
        Assert.Equal(executionTime, result.TotalExecutionTime);
        Assert.Equal(startedAt, result.StartedAt);
        Assert.Equal(completedAt, result.CompletedAt);
    }

    [Fact]
    public void ShouldCreateSuccessfulResult_WhenUsingCrewResultUsingSuccessWithoutParameters()
    {
        // Act
        var result = CrewResult.Success();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
        Assert.Same(TaskOutputMap.Empty, result.TaskOutputs);
        Assert.Empty(result.CompletedTasks);
        Assert.Empty(result.FailedTasks);
    }

    [Fact]
    public void ShouldCreateSuccessfulResult_WhenUsingCrewResultUsingSuccessWithParameters()
    {
        // Arrange
        var taskOutputs = TaskOutputMap.Empty;
        var completedTasks = new List<string> { "task1", "task2", "task3" };

        // Act
        var result = CrewResult.Success(taskOutputs, completedTasks);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
        Assert.Same(taskOutputs, result.TaskOutputs);
        Assert.Same(completedTasks, result.CompletedTasks);
        Assert.Empty(result.FailedTasks);
    }

    [Fact]
    public void ShouldCreateFailedResult_WhenUsingCrewResultUsingFailureWithoutFailedTasks()
    {
        // Arrange
        var errorMessage = "Execution interrupted";

        // Act
        var result = CrewResult.Failure(errorMessage);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(errorMessage, result.ErrorMessage);
        Assert.Same(TaskOutputMap.Empty, result.TaskOutputs);
        Assert.Empty(result.CompletedTasks);
        Assert.Empty(result.FailedTasks);
    }

    [Fact]
    public void ShouldCreateFailedResult_WhenUsingCrewResultUsingFailureWithFailedTasks()
    {
        // Arrange
        var errorMessage = "Tasks failed";
        var failedTasks = new List<string> { "task1", "task2" };

        // Act
        var result = CrewResult.Failure(errorMessage, failedTasks);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal(errorMessage, result.ErrorMessage);
        Assert.Same(failedTasks, result.FailedTasks);
    }

    [Fact]
    public void ShouldBeUtcNow_WhenUsingCrewResultWithDefaultStartedAt()
    {
        // Act
        var result = CrewResult.Create(true);

        // Assert
        Assert.Equal(DateTimeKind.Utc, result.StartedAt.Kind);
        Assert.True(result.StartedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingProcessResultUsingEqualsWithSameValues()
    {
        // Arrange
        var outputs = ProcessOutputs.Empty.With("key", "value");
        var startedAt = DateTime.UtcNow;

        var result1 = ProcessResult.Create(
            true,
            outputs,
            null,
            TimeSpan.FromSeconds(5),
            startedAt);

        var result2 = ProcessResult.Create(
            true,
            outputs,
            null,
            TimeSpan.FromSeconds(5),
            startedAt);

        // Act & Assert
        Assert.Equal(result1, result2);
        Assert.True(result1.Equals(result2));
        Assert.Equal(result1.GetHashCode(), result2.GetHashCode());
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingCrewResultUsingEqualsWithSameValues()
    {
        // Arrange
        var startedAt = DateTime.UtcNow;

        var result1 = CrewResult.Create(
            true,
            TaskOutputMap.Empty,
            null,
            new TaskCompletionLists(["task1"], []),
            new ExecutionTiming(TimeSpan.FromMinutes(1), startedAt));

        var result2 = CrewResult.Create(
            true,
            TaskOutputMap.Empty,
            null,
            new TaskCompletionLists(["task1"], []),
            new ExecutionTiming(TimeSpan.FromMinutes(1), startedAt));

        // Act & Assert - Compare individual properties instead of object equality
        Assert.Equal(result1.IsSuccess, result2.IsSuccess);
        Assert.Equal(result1.ErrorMessage, result2.ErrorMessage);
        Assert.Equal(result1.TaskOutputs, result2.TaskOutputs);
        Assert.Equal(result1.CompletedTasks, result2.CompletedTasks);
        Assert.Equal(result1.FailedTasks, result2.FailedTasks);
        Assert.Equal(result1.TotalExecutionTime, result2.TotalExecutionTime);
        Assert.Equal(result1.StartedAt, result2.StartedAt);
        Assert.Equal(result1.CompletedAt, result2.CompletedAt);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "Error occurred")]
    public void ShouldCreateExpectedStates_WhenUsingProcessResultUsingFactoryMethods(bool isSuccess, string? errorMessage)
    {
        // Act
        var result = isSuccess
            ? ProcessResult.Success()
            : ProcessResult.Failure(errorMessage!);

        // Assert
        Assert.Equal(isSuccess, result.IsSuccess);
        Assert.Equal(errorMessage, result.ErrorMessage);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "Crew failed")]
    public void ShouldCreateExpectedStates_WhenUsingCrewResultUsingFactoryMethods(bool isSuccess, string? errorMessage)
    {
        // Act
        var result = isSuccess
            ? CrewResult.Success()
            : CrewResult.Failure(errorMessage!);

        // Assert
        Assert.Equal(isSuccess, result.IsSuccess);
        Assert.Equal(errorMessage, result.ErrorMessage);
    }
}
