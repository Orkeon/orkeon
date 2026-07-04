using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Tests.Fixtures;

namespace Orkeon.Domain.Tests.Crew;

public class CrewExecutionTests
{
    [Fact]
    public void ShouldInitializeExecution_WhenConstructingWithValidParameters()
    {
        // Arrange
        var processId = ProcessId.Create();
        var startedAt = DateTime.UtcNow;

        // Act
        var execution = new CrewExecution(processId, startedAt);

        // Assert
        Assert.NotNull(execution);
        Assert.Equal(processId, execution.ProcessId);
        Assert.Equal(startedAt, execution.StartedAt);
        Assert.Equal(ExecutionStatus.Running, execution.Status);
        Assert.Equal(0, execution.CompletedTasks);
        Assert.Equal(0, execution.FailedTasks);
        Assert.Null(execution.CompletedAt);
        Assert.Null(execution.Duration);
        Assert.Null(execution.FailureReason);
        Assert.Equal(0, execution.SuccessRate);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullProcessId()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new CrewExecution(null!, DateTime.UtcNow));
        Assert.Equal("processId", exception.ParamName);
    }

    [Fact]
    public void ShouldMarkAsSucceeded_WhenCompletingWithValidTaskCounts()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);
        var completedTasks = 5;
        var failedTasks = 0;

        // Act
        execution.Complete(completedTasks, failedTasks);

        // Assert
        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(completedTasks, execution.CompletedTasks);
        Assert.Equal(failedTasks, execution.FailedTasks);
        Assert.NotNull(execution.CompletedAt);
        Assert.NotNull(execution.Duration);
        Assert.True(execution.Duration.Value.TotalMilliseconds >= 0);
        Assert.Equal(100, execution.SuccessRate);
    }

    [Fact]
    public void ShouldMarkAsPartialSuccess_WhenCompletingWithSomeFailedTasks()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);
        var completedTasks = 3;
        var failedTasks = 2;

        // Act
        execution.Complete(completedTasks, failedTasks);

        // Assert
        Assert.Equal(ExecutionStatus.PartialSuccess, execution.Status);
        Assert.Equal(completedTasks, execution.CompletedTasks);
        Assert.Equal(failedTasks, execution.FailedTasks);
        Assert.Equal(60, execution.SuccessRate); // 3/5 * 100 = 60%
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenCompletingWhenNotRunning()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);
        execution.Complete(1, 0); // First completion

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => execution.Complete(2, 0));
        Assert.Contains("Cannot complete execution in Succeeded status", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCompletingWithNegativeCompletedTasks()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => execution.Complete(-1, 0));
        Assert.Equal("completedTasks", exception.ParamName);
        Assert.Contains("cannot be negative", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCompletingWithNegativeFailedTasks()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => execution.Complete(1, -1));
        Assert.Equal("failedTasks", exception.ParamName);
        Assert.Contains("cannot be negative", exception.Message);
    }

    [Fact]
    public void ShouldMarkAsFailed_WhenFailingWithValidReason()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);
        var reason = "Critical error occurred during task execution";

        // Act
        execution.Fail(reason);

        // Assert
        Assert.Equal(ExecutionStatus.Failed, execution.Status);
        Assert.Equal(reason, execution.FailureReason);
        Assert.NotNull(execution.CompletedAt);
        Assert.NotNull(execution.Duration);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenFailingWhenNotRunning()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);
        execution.Fail("First failure");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => execution.Fail("Second failure"));
        Assert.Contains("Cannot fail execution in Failed status", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenFailingWithEmptyReason()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => execution.Fail(""));
        Assert.Equal("reason", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenFailingWithWhitespaceReason()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);

        // Act & Assert
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => execution.Fail("   "));
        Assert.Equal("reason", exception.ParamName);
    }

    [Fact]
    public void ShouldUpdateTaskCounts_WhenUpdatingProgressWithValidCounts()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);

        // Act
        execution.UpdateProgress(2, 1);

        // Assert
        Assert.Equal(2, execution.CompletedTasks);
        Assert.Equal(1, execution.FailedTasks);
        Assert.Equal(ExecutionStatus.Running, execution.Status); // Still running
        Assert.Null(execution.CompletedAt); // Not completed yet
    }

    [Fact]
    public void ShouldUpdateToLatestValues_WhenUpdatingProgressWithMultipleTimes()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);

        // Act
        execution.UpdateProgress(1, 0);
        execution.UpdateProgress(2, 0);
        execution.UpdateProgress(3, 1);

        // Assert
        Assert.Equal(3, execution.CompletedTasks);
        Assert.Equal(1, execution.FailedTasks);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenUpdatingProgressWhenNotRunning()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);
        execution.Complete(1, 0);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => execution.UpdateProgress(2, 0));
        Assert.Contains("Cannot update progress for execution in Succeeded status", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUpdatingProgressWithNegativeCompletedTasks()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => execution.UpdateProgress(-1, 0));
        Assert.Equal("completedTasks", exception.ParamName);
        Assert.Contains("cannot be negative", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUpdatingProgressWithNegativeFailedTasks()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => execution.UpdateProgress(1, -1));
        Assert.Equal("failedTasks", exception.ParamName);
        Assert.Contains("cannot be negative", exception.Message);
    }

    [Fact]
    public void ShouldReturnZero_WhenUsingSuccessRateWithNoTasks()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);

        // Act & Assert
        Assert.Equal(0, execution.SuccessRate);
    }

    [Fact]
    public void ShouldReturn100_WhenUsingSuccessRateWithAllTasksCompleted()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);
        execution.UpdateProgress(10, 0);

        // Act & Assert
        Assert.Equal(100, execution.SuccessRate);
    }

    [Fact]
    public void ShouldCalculateCorrectly_WhenUsingSuccessRateWithMixedResults()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);

        // Test various scenarios
        var testCases = new[]
        {
            (completed: 1, failed: 1, expected: 50.0),
            (completed: 3, failed: 1, expected: 75.0),
            (completed: 1, failed: 3, expected: 25.0),
            (completed: 7, failed: 3, expected: 70.0)
        };

        foreach (var (completed, failed, expected) in testCases)
        {
            // Act
            execution.UpdateProgress(completed, failed);

            // Assert
            Assert.Equal(expected, execution.SuccessRate);
        }
    }

    [Fact]
    public void ShouldBeNull_WhenUsingDurationBeforeCompletion()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);

        // Act & Assert
        Assert.Null(execution.Duration);
    }

    [Fact]
    public void ShouldBeCalculated_WhenUsingDurationAfterCompletion()
    {
        // Arrange
        var startTime = DateTime.UtcNow;
        var execution = new CrewExecution(ProcessId.Create(), startTime);

        // Ensure duration > 0 (deterministic clock advance, R5.6)
        ClockAdvance.UntilStrictlyAfter(startTime);

        // Act
        execution.Complete(1, 0);

        // Assert
        Assert.NotNull(execution.Duration);
        Assert.True(execution.Duration.Value.TotalMilliseconds > 0);
        Assert.True(execution.Duration.Value.TotalSeconds < 1); // Should be very quick
    }

    [Fact]
    public void ShouldBeCalculated_WhenUsingDurationAfterFailure()
    {
        // Arrange
        var startTime = DateTime.UtcNow;
        var execution = new CrewExecution(ProcessId.Create(), startTime);

        // Ensure duration > 0 (deterministic clock advance, R5.6)
        ClockAdvance.UntilStrictlyAfter(startTime);

        // Act
        execution.Fail("Test failure");

        // Assert
        Assert.NotNull(execution.Duration);
        Assert.True(execution.Duration.Value.TotalMilliseconds > 0);
    }
}
