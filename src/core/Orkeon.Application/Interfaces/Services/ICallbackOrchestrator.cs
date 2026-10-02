
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Task;
using Orkeon.Application.Callback;

namespace Orkeon.Application.Interfaces.Services;

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
    /// The turns the agent loop ran for the task — each a model call, with the tool calls it asked
    /// for (<see cref="TaskResult.IterationsUsed"/>). Zero when the task ended before its first turn.
    /// </summary>
    public int StepsExecuted { get; init; }

    /// <summary>
    /// When the task execution started.
    /// </summary>
    public DateTime StartTime { get; init; }
}

/// <summary>
/// Calls every registered <see cref="ICallbackHandler"/> during a crew run: task start and end
/// around each agent execution (<c>AgentExecutionService</c>), step start and end around each
/// tool call of the agent loops (<see cref="Orkeon.Application.Execution.StepNotifyingToolInvocationPipeline"/>).
/// A handler that throws is logged and skipped.
/// </summary>
public interface ICallbackOrchestrator
{
    /// <summary>
    /// Notifies all registered handlers that a task has started.
    /// </summary>
    System.Threading.Tasks.Task NotifyTaskStartedAsync(
        DomainAgent agent,
        CrewTask task,
        DateTime startTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies all registered handlers that a task has completed, successfully or not.
    /// </summary>
    System.Threading.Tasks.Task NotifyTaskCompletedAsync(
        DomainAgent agent,
        CrewTask task,
        TaskCompletionInfo completionInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies all registered handlers that an agent step — one tool call — is starting.
    /// </summary>
    System.Threading.Tasks.Task NotifyStepStartedAsync(
        StepStartedContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies all registered handlers that an agent step — one tool call — has ended.
    /// </summary>
    System.Threading.Tasks.Task NotifyStepCompletedAsync(
        StepCompletedContext context,
        CancellationToken cancellationToken = default);
}
