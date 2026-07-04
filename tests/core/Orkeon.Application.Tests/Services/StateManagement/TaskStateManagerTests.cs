using Microsoft.Extensions.Logging;
using Orkeon.Application.Services.StateManagement;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Services.StateManagement;

public class TaskStateManagerTests
{
    private readonly TaskStateManager _taskStateManager;
    private readonly TestLogger<TaskStateManager> _logger;

    public TaskStateManagerTests()
    {
        _logger = new TestLogger<TaskStateManager>();
        _taskStateManager = new TaskStateManager(_logger);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullLogger()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new TaskStateManager(null!));
        Assert.Equal("logger", exception.ParamName);
    }

    #region Task State Transition Tests

    [Theory]
    [InlineData(Pending, TaskStateEvent.Start, InProgress)]
    [InlineData(InProgress, TaskStateEvent.Complete, Completed)]
    [InlineData(InProgress, TaskStateEvent.Fail, Failed)]
    [InlineData(InProgress, TaskStateEvent.Cancel, "Cancelled")]
    [InlineData(InProgress, TaskStateEvent.Block, "Blocked")]
    [InlineData("Blocked", TaskStateEvent.Unblock, InProgress)]
    [InlineData(Failed, TaskStateEvent.Retry, InProgress)]
    public void ShouldSucceed_WhenUsingTransitionTaskStateWithValidTransitions(
        string currentStateStr,
        TaskStateEvent stateEvent,
        string expectedStateStr)
    {
        // Arrange
        var currentState = TaskStatus.From(currentStateStr);
        var expectedState = TaskStatus.From(expectedStateStr);
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            RetryCount: 0,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeoutStandard);

        // Act
        var result = _taskStateManager.TransitionTaskState(currentState, stateEvent, context);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(expectedState, result.ToState);
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData(Completed, TaskStateEvent.Start, "Task has already completed")]
    [InlineData("Cancelled", TaskStateEvent.Complete, "Cannot complete cancelled task")]
    [InlineData(Pending, TaskStateEvent.Complete, "Task must be in progress to complete")]
    [InlineData(InProgress, TaskStateEvent.Start, "Task is already in progress")]
    [InlineData("Blocked", TaskStateEvent.Complete, "Cannot complete blocked task")]
    public void ShouldFail_WhenUsingTransitionTaskStateWithInvalidTransitions(
        string currentStateStr,
        TaskStateEvent stateEvent,
        string expectedReason)
    {
        // Arrange
        var currentState = TaskStatus.From(currentStateStr);
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            RetryCount: 0,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeoutStandard);

        // Act
        var result = _taskStateManager.TransitionTaskState(currentState, stateEvent, context);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(currentState, result.ToState);
        Assert.Contains(expectedReason, result.ErrorMessage);
    }

    [Fact]
    public void ShouldFail_WhenUsingTransitionTaskStateStartingWithIncompleteDependencies()
    {
        // Arrange
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: false,
            CanResume: false,
            RetryCount: 0,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeSpan.Zero);

        // Act
        var result = _taskStateManager.TransitionTaskState(TaskStatus.Pending, TaskStateEvent.Start, context);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(TaskStatus.Pending, result.ToState);
        Assert.Contains("Dependencies not complete", result.ErrorMessage);
    }

    [Fact]
    public void ShouldFail_WhenUsingTransitionTaskStateRetryingExceedsMaxRetries()
    {
        // Arrange
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            RetryCount: 3,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeoutStandard);

        // Act
        var result = _taskStateManager.TransitionTaskState(TaskStatus.Failed, TaskStateEvent.Retry, context);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(TaskStatus.Failed, result.ToState);
        Assert.Contains("Max retries exceeded", result.ErrorMessage);
    }

    #endregion

    #region DetermineNextAction Tests

    [Theory]
    [InlineData(Pending, false, true, TaskAction.StartExecution)]
    [InlineData(Pending, true, false, TaskAction.WaitForPrerequisites)]
    [InlineData(InProgress, false, true, TaskAction.ContinueExecution)]
    [InlineData(Completed, false, true, TaskAction.NoAction)]
    [InlineData(Failed, false, true, TaskAction.ScheduleRetry)]
    [InlineData("Blocked", false, true, TaskAction.Unblock)]
    [InlineData("Cancelled", false, true, TaskAction.NoAction)]
    public void ShouldReturnCorrectAction_WhenDeterminingNextActionWithVariousStatesAndContexts(
        string currentStateStr,
        bool hasDependencies,
        bool dependenciesComplete,
        TaskAction expectedAction)
    {
        // Arrange
        var currentState = TaskStatus.From(currentStateStr);
        // Use hasDependencies parameter to prevent xUnit1026 warning
        _ = hasDependencies;
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: dependenciesComplete,
            CanResume: false,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            RetryCount: 0,
            ExecutionTime: TimeoutStandard);

        // Act
        var action = _taskStateManager.DetermineNextAction(currentState, context);

        // Assert
        Assert.Equal(expectedAction, action);
    }

    [Fact]
    public void ShouldReturnAbandon_WhenDeterminingNextActionFailedTaskWithMaxRetriesExceeded()
    {
        // Arrange
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            RetryCount: 3,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeoutStandard);

        // Act
        var action = _taskStateManager.DetermineNextAction(TaskStatus.Failed, context);

        // Assert
        Assert.Equal(TaskAction.ScheduleRetry, action);
    }

    [Fact]
    public void ShouldReturnReview_WhenDeterminingNextActionInProgressTaskExceedingTimeLimit()
    {
        // Arrange
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            RetryCount: 0,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeSpan.FromHours(2)); // Exceeds 1 hour threshold

        // Act
        var action = _taskStateManager.DetermineNextAction(TaskStatus.InProgress, context);

        // Assert
        Assert.Equal(TaskAction.RequestReview, action);
    }

    #endregion

    #region GetPriorityAdjustment Tests

    [Theory]
    [InlineData("Blocked", true, PriorityAdjustment.Increase)]
    [InlineData("Blocked", false, PriorityAdjustment.None)]
    [InlineData(Failed, true, PriorityAdjustment.Increase)]
    [InlineData(Failed, false, PriorityAdjustment.None)]
    [InlineData(InProgress, false, PriorityAdjustment.None)]
    [InlineData(Completed, false, PriorityAdjustment.None)]
    [InlineData(Pending, true, PriorityAdjustment.None)]
    public void ShouldReturnCorrectAdjustment_WhenGettingPriorityAdjustmentWithVariousStatesAndPriorities(
        string currentStateStr,
        bool isHighPriority,
        PriorityAdjustment expectedAdjustment)
    {
        // Arrange
        var currentState = TaskStatus.From(currentStateStr);
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: isHighPriority,  // Use isHighPriority for deadline logic
            RetryCount: 1,
            ExecutionTime: TimeoutStandard);

        // Act
        var adjustment = TaskStateManager.GetPriorityAdjustment(currentState, context);

        // Assert
        Assert.Equal(expectedAdjustment, adjustment);
    }

    [Fact]
    public void ShouldReturnDecrease_WhenGettingPriorityAdjustmentInProgressLongRunning()
    {
        // Arrange
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            RetryCount: 0,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeSpan.FromHours(3)); // Very long running

        // Act
        var adjustment = TaskStateManager.GetPriorityAdjustment(TaskStatus.InProgress, context);

        // Assert
        Assert.Equal(PriorityAdjustment.Decrease, adjustment);
    }

    [Fact]
    public void ShouldReturnIncrease_WhenGettingPriorityAdjustmentFailedMultipleRetries()
    {
        // Arrange
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            RetryCount: 2,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeoutStandard);

        // Act
        var adjustment = TaskStateManager.GetPriorityAdjustment(TaskStatus.Failed, context);

        // Assert
        Assert.Equal(PriorityAdjustment.Increase, adjustment);
    }

    #endregion

    #region Logging Tests

    [Fact]
    public void ShouldLogInformation_WhenUsingTransitionTaskStateUsingSuccessfulTransition()
    {
        // Arrange
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            RetryCount: 0,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeSpan.Zero);

        // Act
        _taskStateManager.TransitionTaskState(TaskStatus.Pending, TaskStateEvent.Start, context);

        // Assert
        Assert.Contains(_logger.LoggedMessages, m =>
            m.LogLevel == LogLevel.Information &&
            m.Message.Contains("Task state transition") &&
            m.Message.Contains("Pending -> InProgress"));
    }

    [Fact]
    public void ShouldLogWarning_WhenUsingTransitionTaskStateUsingFailedTransition()
    {
        // Arrange
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: false,
            CanResume: false,
            RetryCount: 0,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeSpan.Zero);

        // Act
        _taskStateManager.TransitionTaskState(TaskStatus.Pending, TaskStateEvent.Start, context);

        // Assert
        Assert.Contains(_logger.LoggedMessages, m =>
            m.LogLevel == LogLevel.Warning &&
            m.Message.Contains("Invalid task state transition") &&
            m.Message.Contains("Dependencies not complete"));
    }

    [Fact]
    public void ShouldLogWarning_WhenDeterminingNextActionHighPriorityBlocked()
    {
        // Arrange
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            RetryCount: 0,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeoutStandard);

        // Act
        _taskStateManager.DetermineNextAction(TaskStatus.Blocked, context);

        // Assert
        Assert.Contains(_logger.LoggedMessages, m =>
            m.LogLevel == LogLevel.Warning &&
            m.Message.Contains("High priority task blocked"));
    }

    #endregion

    #region Complex Workflow Tests

    [Fact]
    public void ShouldTransitionCorrectly_WhenUsingTaskLifecycleNormalFlow()
    {
        // Arrange
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            RetryCount: 0,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeSpan.Zero);

        var state = TaskStatus.Pending;

        // Act & Assert - Normal task lifecycle
        var result1 = _taskStateManager.TransitionTaskState(state, TaskStateEvent.Start, context);
        Assert.True(result1.IsValid);
        Assert.Equal(TaskStatus.InProgress, result1.ToState);

        var result2 = _taskStateManager.TransitionTaskState(result1.ToState, TaskStateEvent.Complete, context);
        Assert.True(result2.IsValid);
        Assert.Equal(TaskStatus.Completed, result2.ToState);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTaskLifecycleWithBlockingAndUnblocking()
    {
        // Arrange
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            RetryCount: 0,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeoutExtended);

        // Act
        var result1 = _taskStateManager.TransitionTaskState(TaskStatus.InProgress, TaskStateEvent.Block, context);
        Assert.True(result1.IsValid);
        Assert.Equal(TaskStatus.Blocked, result1.ToState);

        var action = _taskStateManager.DetermineNextAction(result1.ToState, context);
        Assert.Equal(TaskAction.Schedule, action);

        var result2 = _taskStateManager.TransitionTaskState(result1.ToState, TaskStateEvent.Unblock, context);
        Assert.True(result2.IsValid);
        Assert.Equal(TaskStatus.InProgress, result2.ToState);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTaskLifecycleWithRetryFlow()
    {
        // Arrange & Act
        var context1 = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            RetryCount: 0,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeoutStandard);

        var result1 = _taskStateManager.TransitionTaskState(TaskStatus.InProgress, TaskStateEvent.Fail, context1);
        Assert.True(result1.IsValid);
        Assert.Equal(TaskStatus.Failed, result1.ToState);

        // First retry
        var context2 = context1 with { RetryCount = 1 };
        var result2 = _taskStateManager.TransitionTaskState(result1.ToState, TaskStateEvent.Retry, context2);
        Assert.True(result2.IsValid);
        Assert.Equal(TaskStatus.InProgress, result2.ToState);

        // Fail again
        var result3 = _taskStateManager.TransitionTaskState(result2.ToState, TaskStateEvent.Fail, context2);
        Assert.True(result3.IsValid);
        Assert.Equal(TaskStatus.Failed, result3.ToState);

        // Max retries exceeded
        var context3 = context1 with { RetryCount = 3 };
        var result4 = _taskStateManager.TransitionTaskState(result3.ToState, TaskStateEvent.Retry, context3);
        Assert.False(result4.IsValid);
        Assert.Equal(TaskStatus.Failed, result4.ToState);

        var action = _taskStateManager.DetermineNextAction(result4.ToState, context3);
        Assert.Equal(TaskAction.ScheduleRetry, action);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTaskLifecycleDependencyFlow()
    {
        // Arrange
        var contextWithDeps = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: false,
            CanResume: false,
            RetryCount: 0,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeSpan.Zero);

        // Act & Assert - Cannot start with incomplete dependencies
        var result1 = _taskStateManager.TransitionTaskState(TaskStatus.Pending, TaskStateEvent.Start, contextWithDeps);
        Assert.False(result1.IsValid);

        var action1 = _taskStateManager.DetermineNextAction(TaskStatus.Pending, contextWithDeps);
        Assert.Equal(TaskAction.WaitForPrerequisites, action1);

        // Dependencies complete
        var contextDepsComplete = contextWithDeps with { PrerequisitesMet = true };
        var result2 = _taskStateManager.TransitionTaskState(TaskStatus.Pending, TaskStateEvent.Start, contextDepsComplete);
        Assert.True(result2.IsValid);
        Assert.Equal(TaskStatus.InProgress, result2.ToState);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldReturnInvalid_WhenUsingTransitionTaskStateUsingUnhandledStateEventCombination()
    {
        // Arrange
        var context = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            RetryCount: 0,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeSpan.Zero);

        // Act - Try to unblock a non-blocked task
        var result = _taskStateManager.TransitionTaskState(TaskStatus.Pending, TaskStateEvent.Unblock, context);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Invalid state transition", result.ErrorMessage);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenGettingPriorityAdjustmentEdgeCases()
    {
        // Test with exactly 1 hour execution time (boundary)
        var context1Hour = new TaskExecutionContext(
            AgentAvailable: true,
            PrerequisitesMet: true,
            CanResume: false,
            RetryCount: 0,
            CanRetry: true,
            OutputValid: false,
            RequiresReview: false,
            ShouldPause: false,
            IsNearCompletion: false,
            IsNearDeadline: false,
            ExecutionTime: TimeSpan.FromHours(1));

        var adjustment1 = TaskStateManager.GetPriorityAdjustment(TaskStatus.InProgress, context1Hour);
        Assert.Equal(PriorityAdjustment.None, adjustment1);

        // Test with just over 2 hours (should decrease)
        var context2Hours = context1Hour with { ExecutionTime = TimeSpan.FromHours(2).Add(TimeSpan.FromMinutes(1)) };
        var adjustment2 = TaskStateManager.GetPriorityAdjustment(TaskStatus.InProgress, context2Hours);
        Assert.Equal(PriorityAdjustment.Decrease, adjustment2);
    }

    #endregion
}

