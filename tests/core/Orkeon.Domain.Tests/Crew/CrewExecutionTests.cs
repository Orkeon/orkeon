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
        Assert.Null(execution.CompletedAt);
        Assert.Null(execution.Duration);
        Assert.Null(execution.FailureReason);
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

        // Act
        execution.Complete(completedTasks);

        // Assert
        Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(completedTasks, execution.CompletedTasks);
        Assert.NotNull(execution.CompletedAt);
        Assert.NotNull(execution.Duration);
        Assert.True(execution.Duration.Value.TotalMilliseconds >= 0);
    }

    [Fact]
    public void A_failed_execution_completed_no_task_and_its_reason_says_what_failed()
    {
        // GAP-32: a run with a task that did not succeed fails — it is never a partial success, and
        // its reason, not a count, names the tasks that did not succeed.
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);

        execution.Fail("Task review (Writer) failed: no final answer");

        Assert.Equal(ExecutionStatus.Failed, execution.Status);
        Assert.Equal(0, execution.CompletedTasks);
        Assert.Equal("Task review (Writer) failed: no final answer", execution.FailureReason);
    }

    [Fact]
    public void ShouldThrowInvalidOperationException_WhenCompletingWhenNotRunning()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);
        execution.Complete(1); // First completion

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => execution.Complete(2));
        Assert.Contains("Cannot complete execution in Succeeded status", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenCompletingWithNegativeCompletedTasks()
    {
        // Arrange
        var execution = new CrewExecution(ProcessId.Create(), DateTime.UtcNow);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => execution.Complete(-1));
        Assert.Equal("completedTasks", exception.ParamName);
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
        execution.Complete(1);

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
