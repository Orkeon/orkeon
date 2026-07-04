
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Task;
using Orkeon.Application.Callback;

namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// Groups optional callback handlers for notification methods.
/// </summary>
public sealed class CallbackHandlers
{
    /// <summary>
    /// Optional agent-level callback handler.
    /// </summary>
    public ICallbackHandler? AgentCallbackHandler { get; init; }

    /// <summary>
    /// Optional task-level callback handler.
    /// </summary>
    public ICallbackHandler? TaskCallbackHandler { get; init; }

    /// <summary>
    /// Optional task lifecycle callbacks.
    /// </summary>
    public TaskCallbacks? TaskCallbacks { get; init; }

    /// <summary>
    /// Empty instance with no handlers.
    /// </summary>
    public static readonly CallbackHandlers None = new();
}

/// <summary>
/// Information about a completed task for callback notification.
/// </summary>
public sealed class TaskCompletionInfo
{
    /// <summary>
    /// The task execution result.
    /// </summary>
    public TaskResult Result { get; init; } = null!;

    /// <summary>
    /// Number of steps executed.
    /// </summary>
    public int StepsExecuted { get; init; }

    /// <summary>
    /// When the task execution started.
    /// </summary>
    public DateTime StartTime { get; init; }
}

/// <summary>
/// Information about step progress for callback notification.
/// </summary>
public sealed class StepProgressInfo
{
    /// <summary>
    /// Description of the current step.
    /// </summary>
    public string StepDescription { get; init; } = null!;

    /// <summary>
    /// Current step number.
    /// </summary>
    public int CurrentStep { get; init; }

    /// <summary>
    /// Total number of steps.
    /// </summary>
    public int TotalSteps { get; init; }
}

/// <summary>
/// Orchestrates callback notifications during task execution.
/// Separates callback handling from execution logic.
/// </summary>
public interface ICallbackOrchestrator
{
    /// <summary>
    /// Notifies all relevant handlers that a task has started.
    /// </summary>
    System.Threading.Tasks.Task NotifyTaskStartedAsync(
        DomainAgent agent,
        CrewTask task,
        DateTime startTime,
        CallbackHandlers? handlers = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies all relevant handlers that a task has completed.
    /// </summary>
    System.Threading.Tasks.Task NotifyTaskCompletedAsync(
        DomainAgent agent,
        CrewTask task,
        TaskCompletionInfo completionInfo,
        CallbackHandlers? handlers = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies handlers of step progression during task execution.
    /// </summary>
    System.Threading.Tasks.Task NotifyStepProgressAsync(
        DomainAgent agent,
        CrewTask task,
        StepProgressInfo progressInfo,
        CallbackHandlers? handlers = null,
        CancellationToken cancellationToken = default);
}
