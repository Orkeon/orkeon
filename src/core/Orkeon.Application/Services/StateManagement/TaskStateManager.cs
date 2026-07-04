using Microsoft.Extensions.Logging;
using Orkeon.Domain.Task;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Application.Services.StateManagement;

/// <summary>
/// Task state manager using pattern matching for state transitions.
/// Phase 3.2.2: Pattern matching for task lifecycle management.
/// Uses Domain Value Objects <see cref="RetryPolicy"/> and <see cref="ExecutionTimePolicy"/>
/// instead of hard-coded business thresholds.
/// </summary>
public partial class TaskStateManager
{
    private readonly ILogger<TaskStateManager> _logger;
    private readonly RetryPolicy _retryPolicy;
    private readonly ExecutionTimePolicy _executionTimePolicy;

    /// <summary>
    /// Initializes a new instance of <see cref="TaskStateManager"/> with default policies.
    /// </summary>
    public TaskStateManager(ILogger<TaskStateManager> logger)
        : this(logger, RetryPolicy.Default, ExecutionTimePolicy.Default)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="TaskStateManager"/> with configurable policies.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="retryPolicy">The retry policy defining retry thresholds.</param>
    /// <param name="executionTimePolicy">The execution time policy defining time thresholds.</param>
    public TaskStateManager(
        ILogger<TaskStateManager> logger,
        RetryPolicy retryPolicy,
        ExecutionTimePolicy executionTimePolicy)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        ArgumentNullException.ThrowIfNull(retryPolicy);
        _retryPolicy = retryPolicy;
        ArgumentNullException.ThrowIfNull(executionTimePolicy);
        _executionTimePolicy = executionTimePolicy;
    }

    /// <summary>
    /// Transitions task state using pattern matching validation.
    /// </summary>
    public StateTransitionResult<TaskStatus> TransitionTaskState(
        TaskStatus currentState,
        TaskStateEvent stateEvent,
        TaskExecutionContext context)
    {
        // First check for invalid transitions with specific error messages
        var errorMessage = (currentState, stateEvent, context) switch
        {
            ({ Value: "Completed" }, TaskStateEvent.Start, _) => "Task has already completed",
            ({ Value: "Cancelled" }, TaskStateEvent.Complete, _) => "Cannot complete cancelled task",
            ({ Value: "Pending" }, TaskStateEvent.Complete, _) => "Task must be in progress to complete",
            ({ Value: "InProgress" }, TaskStateEvent.Start, _) => "Task is already in progress",
            ({ Value: "Blocked" }, TaskStateEvent.Complete, _) => "Cannot complete blocked task",
            ({ Value: "Pending" }, TaskStateEvent.Start, var ctx) when !ctx.PrerequisitesMet => "Dependencies not complete",
            ({ Value: "Failed" }, TaskStateEvent.Retry, var ctx) when !_retryPolicy.ShouldRetry(ctx.RetryCount) => "Max retries exceeded",
            ({ Value: "Pending" }, TaskStateEvent.Unblock, _) => "Invalid state transition",
            _ => null
        };

        if (errorMessage != null)
        {
            LogInvalidTaskTransition(currentState, stateEvent, errorMessage);

            return new StateTransitionResult<TaskStatus>(
                currentState,
                currentState,
                stateEvent,
                false,
                errorMessage);
        }

        var newState = (currentState, stateEvent, context) switch
        {
            // Initial state transitions
            ({ Value: "Pending" }, TaskStateEvent.Start, var ctx) when ctx.PrerequisitesMet
                => TaskStatus.InProgress,

            // Execution states
            ({ Value: "InProgress" }, TaskStateEvent.Complete, _)
                => TaskStatus.Completed,
            ({ Value: "InProgress" }, TaskStateEvent.Fail, _)
                => TaskStatus.Failed,
            ({ Value: "InProgress" }, TaskStateEvent.Cancel, _)
                => TaskStatus.Cancelled,
            ({ Value: "InProgress" }, TaskStateEvent.Block, _)
                => TaskStatus.Blocked,

            // Blocking and unblocking
            ({ Value: "Blocked" }, TaskStateEvent.Unblock, _)
                => TaskStatus.InProgress,

            // Retry logic
            ({ Value: "Failed" }, TaskStateEvent.Retry, var ctx) when _retryPolicy.ShouldRetry(ctx.RetryCount) && ctx.CanRetry
                => TaskStatus.InProgress,

            // Cancellation (from most states)
            (var s, TaskStateEvent.Cancel, _) when s != TaskStatus.Cancelled && s != TaskStatus.Completed
                => TaskStatus.Cancelled,

            // Invalid transitions (stay in current state)
            _ => currentState
        };

        var isValid = newState != currentState;

        if (isValid && _logger.IsEnabled(LogLevel.Information))
        {
            LogTaskStateTransition(currentState, newState, stateEvent);
        }

        return new StateTransitionResult<TaskStatus>(
            currentState,
            newState,
            stateEvent,
            isValid,
            null);
    }

    /// <summary>
    /// Determines next action using pattern matching on current state.
    /// </summary>
    public TaskAction DetermineNextAction(TaskStatus currentState, TaskExecutionContext context)
    {
        var action = (currentState, context) switch
        {
            ({ Value: "Pending" }, var ctx) when ctx.AgentAvailable && ctx.PrerequisitesMet
                => TaskAction.StartExecution,
            ({ Value: "Pending" }, var ctx) when !ctx.PrerequisitesMet
                => TaskAction.WaitForPrerequisites,

            // For blocked tasks, return Schedule for specific execution times, otherwise Unblock
            ({ Value: "Blocked" }, var ctx) when _executionTimePolicy.ShouldScheduleBlockedTask(ctx.ExecutionTime)
                => TaskAction.Schedule,
            ({ Value: "Blocked" }, _)
                => TaskAction.Unblock,

            ({ Value: "InProgress" }, var ctx) when _executionTimePolicy.IsOverdue(ctx.ExecutionTime)
                => TaskAction.RequestReview,
            ({ Value: "InProgress" }, _)
                => TaskAction.ContinueExecution,

            ({ Value: "Completed" }, _)
                => TaskAction.NoAction,

            ({ Value: "Failed" }, _)
                => TaskAction.ScheduleRetry,

            ({ Value: "Cancelled" }, _)
                => TaskAction.NoAction,

            _ => TaskAction.NoAction
        };

        // Log warning for high priority blocked tasks
        if (currentState == TaskStatus.Blocked)
        {
            LogHighPriorityTaskBlocked();
        }

        return action;
    }

    /// <summary>
    /// Gets task priority adjustment using pattern matching.
    /// Uses default policies when called statically.
    /// </summary>
    public static PriorityAdjustment GetPriorityAdjustment(TaskStatus currentState, TaskExecutionContext context)
    {
        return GetPriorityAdjustment(currentState, context, RetryPolicy.Default, ExecutionTimePolicy.Default);
    }

    /// <summary>
    /// Gets task priority adjustment using pattern matching with configurable policies.
    /// </summary>
    public static PriorityAdjustment GetPriorityAdjustment(
        TaskStatus currentState,
        TaskExecutionContext context,
        RetryPolicy retryPolicy,
        ExecutionTimePolicy executionTimePolicy)
    {
        ArgumentNullException.ThrowIfNull(retryPolicy);
        ArgumentNullException.ThrowIfNull(executionTimePolicy);
        return (currentState, context) switch
        {
            // Increase priority for blocked tasks near deadline
            ({ Value: "Blocked" }, var ctx) when ctx.IsNearDeadline
                => PriorityAdjustment.Increase,

            // Increase priority for failed tasks that have retried multiple times
            ({ Value: "Failed" }, var ctx) when ctx.IsNearDeadline
                => PriorityAdjustment.Increase,
            ({ Value: "Failed" }, var ctx) when retryPolicy.ShouldEscalatePriority(ctx.RetryCount)
                => PriorityAdjustment.Increase,

            // Decrease priority for very long-running tasks
            ({ Value: "InProgress" }, var ctx) when executionTimePolicy.ShouldDeprioritize(ctx.ExecutionTime)
                => PriorityAdjustment.Decrease,

            // No adjustment needed for other states including Pending
            _ => PriorityAdjustment.None
        };
    }


    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid task state transition attempted: {CurrentState} with event {StateEvent}: {Reason}")]
    private partial void LogInvalidTaskTransition(TaskStatus currentState, TaskStateEvent stateEvent, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Task state transition: {CurrentState} -> {NewState} (Event: {StateEvent})")]
    private partial void LogTaskStateTransition(TaskStatus currentState, TaskStatus newState, TaskStateEvent stateEvent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "High priority task blocked")]
    private partial void LogHighPriorityTaskBlocked();
}

/// <summary>
/// Task state events for pattern matching.
/// </summary>
public enum TaskStateEvent
{
    /// <summary>Schedule.</summary>
    Schedule,
    /// <summary>Start.</summary>
    Start,
    /// <summary>Pause.</summary>
    Pause,
    /// <summary>Resume.</summary>
    Resume,
    /// <summary>Complete.</summary>
    Complete,
    /// <summary>Fail.</summary>
    Fail,
    /// <summary>Block.</summary>
    Block,
    /// <summary>Unblock.</summary>
    Unblock,
    /// <summary>Request Review.</summary>
    RequestReview,
    /// <summary>Approve Review.</summary>
    ApproveReview,
    /// <summary>Reject Review.</summary>
    RejectReview,
    /// <summary>Retry.</summary>
    Retry,
    /// <summary>Cancel.</summary>
    Cancel,
    /// <summary>Reset.</summary>
    Reset
}

/// <summary>
/// Flags describing the readiness and capability of a task's execution environment.
/// </summary>
public record TaskReadinessFlags(
    bool AgentAvailable,
    bool PrerequisitesMet,
    bool CanResume,
    bool CanRetry);

/// <summary>
/// Flags describing the current execution status of a task.
/// </summary>
public record TaskStatusFlags(
    bool OutputValid,
    bool RequiresReview,
    bool ShouldPause,
    bool IsNearCompletion,
    bool IsNearDeadline);

/// <summary>
/// Task execution context for pattern matching decisions.
/// Keeps the original positional parameters for backward compatibility with <c>with</c> expressions,
/// and exposes grouped sub-records via computed properties.
/// </summary>
#pragma warning disable S107 // Positional parameters kept for backward-compatible 'with' expressions; use TaskExecutionContext(TaskReadinessFlags, TaskStatusFlags, ...) constructor instead
public record TaskExecutionContext(
    bool AgentAvailable,
    bool PrerequisitesMet,
    bool CanResume,
    bool CanRetry,
    bool OutputValid,
    bool RequiresReview,
    bool ShouldPause,
    bool IsNearCompletion,
    bool IsNearDeadline,
    int RetryCount = 0,
    TimeSpan ExecutionTime = default)
#pragma warning restore S107
{
    /// <summary>
    /// Gets readiness flags as a grouped record.
    /// </summary>
    public TaskReadinessFlags Readiness => new(AgentAvailable, PrerequisitesMet, CanResume, CanRetry);

    /// <summary>
    /// Gets status flags as a grouped record.
    /// </summary>
    public TaskStatusFlags StatusFlags => new(OutputValid, RequiresReview, ShouldPause, IsNearCompletion, IsNearDeadline);

    /// <summary>
    /// Creates a TaskExecutionContext from grouped flag records.
    /// </summary>
    public TaskExecutionContext(
        TaskReadinessFlags readiness,
        TaskStatusFlags status,
        int retryCount = 0,
        TimeSpan executionTime = default)
        : this(
            (readiness ?? throw new ArgumentNullException(nameof(readiness))).AgentAvailable, readiness.PrerequisitesMet, readiness.CanResume, readiness.CanRetry,
            (status ?? throw new ArgumentNullException(nameof(status))).OutputValid, status.RequiresReview, status.ShouldPause, status.IsNearCompletion, status.IsNearDeadline,
            retryCount, executionTime)
    {
    }
}

/// <summary>
/// Recommended actions based on task state.
/// </summary>
public enum TaskAction
{
    /// <summary>No Action.</summary>
    NoAction,
    /// <summary>Schedule.</summary>
    Schedule,
    /// <summary>Start Execution.</summary>
    StartExecution,
    /// <summary>Continue Execution.</summary>
    ContinueExecution,
    /// <summary>Pause.</summary>
    Pause,
    /// <summary>Resume.</summary>
    Resume,
    /// <summary>Finalize Output.</summary>
    FinalizeOutput,
    /// <summary>Request Review.</summary>
    RequestReview,
    /// <summary>Schedule Retry.</summary>
    ScheduleRetry,
    /// <summary>Unblock.</summary>
    Unblock,
    /// <summary>Wait For Agent.</summary>
    WaitForAgent,
    /// <summary>Wait For Prerequisites.</summary>
    WaitForPrerequisites,
    /// <summary>Wait To Resume.</summary>
    WaitToResume,
    /// <summary>Wait For Review.</summary>
    WaitForReview,
    /// <summary>Check Prerequisites.</summary>
    CheckPrerequisites,
    /// <summary>Mark Final Failure.</summary>
    MarkFinalFailure
}

/// <summary>
/// Priority adjustments based on state and context.
/// </summary>
public enum PriorityAdjustment
{
    /// <summary>Decrease.</summary>
    Decrease,
    /// <summary>None.</summary>
    None,
    /// <summary>Increase.</summary>
    Increase
}
