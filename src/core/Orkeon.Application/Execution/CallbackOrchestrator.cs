
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Callback;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Execution;

/// <summary>
/// Orchestrates callbacks for agent and task execution.
/// Dispatches to all registered ICallbackHandler instances and optional per-call handlers.
/// Individual handler failures are logged but do not block execution.
/// </summary>
public partial class CallbackOrchestrator : ICallbackOrchestrator
{
    private readonly ILogger<CallbackOrchestrator> _logger;
    private readonly List<ICallbackHandler> _handlers;

    /// <summary>
    /// Initializes a new instance of <see cref="CallbackOrchestrator"/>.
    /// </summary>
    public CallbackOrchestrator(
        ILogger<CallbackOrchestrator> logger,
        IEnumerable<ICallbackHandler>? handlers = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _handlers = (handlers ?? []).ToList();
    }

    /// <summary>
    /// Notify Task Started Async.
    /// </summary>
    public System.Threading.Tasks.Task NotifyTaskStartedAsync(
        DomainAgent agent,
        CrewTask task,
        DateTime startTime,
        CallbackHandlers? handlers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);

        return NotifyTaskStartedCoreAsync();

        async System.Threading.Tasks.Task NotifyTaskStartedCoreAsync()
        {
            LogTaskStartedByAgent(task.Id, agent.Id, startTime);

            var context = new TaskStartedContext(
                TaskId: task.Id.ToString(),
                Description: task.Description,
                ExpectedOutput: task.ExpectedOutput ?? string.Empty,
                AgentId: agent.Id.ToString(),
                AgentRole: agent.Role.ToString(),
                Timestamp: startTime);

            // Dispatch to all registered handlers
            foreach (var handler in _handlers)
            {
                await InvokeHandlerSafelyAsync(
                    handler, h => h.OnTaskStartedAsync(context, cancellationToken),
                    nameof(ICallbackHandler.OnTaskStartedAsync), cancellationToken).ConfigureAwait(false);
            }

            // Dispatch to optional per-call handlers
            if (handlers?.AgentCallbackHandler != null)
            {
                await InvokeHandlerSafelyAsync(
                    handlers.AgentCallbackHandler, h => h.OnTaskStartedAsync(context, cancellationToken),
                    nameof(ICallbackHandler.OnTaskStartedAsync), cancellationToken).ConfigureAwait(false);
            }

            if (handlers?.TaskCallbackHandler != null)
            {
                await InvokeHandlerSafelyAsync(
                    handlers.TaskCallbackHandler, h => h.OnTaskStartedAsync(context, cancellationToken),
                    nameof(ICallbackHandler.OnTaskStartedAsync), cancellationToken).ConfigureAwait(false);
            }

            // Dispatch to TaskCallbacks
            if (handlers?.TaskCallbacks?.OnStarted != null)
            {
                await InvokeDelegateSafelyAsync(
                    () => handlers.TaskCallbacks.OnStarted(context),
                    "TaskCallbacks.OnStarted", cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Notify Task Completed Async.
    /// </summary>
    public System.Threading.Tasks.Task NotifyTaskCompletedAsync(
        DomainAgent agent,
        CrewTask task,
        TaskCompletionInfo completionInfo,
        CallbackHandlers? handlers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(completionInfo);

        return NotifyTaskCompletedCoreAsync();

        async System.Threading.Tasks.Task NotifyTaskCompletedCoreAsync()
        {
            LogTaskCompletedByAgent(task.Id, agent.Id, completionInfo.Result.Success);

            var duration = DateTime.UtcNow - completionInfo.StartTime;
            var context = new TaskCompletedContext(
                TaskId: task.Id.ToString(),
                AgentId: agent.Id.ToString(),
                Outcome: new TaskExecutionOutcome(completionInfo.Result.Success, completionInfo.Result.Output, completionInfo.Result.StructuredOutput, completionInfo.Result.Error),
                Duration: duration,
                StepsExecuted: completionInfo.StepsExecuted,
                Timestamp: DateTime.UtcNow);

            // Dispatch to all registered handlers
            foreach (var handler in _handlers)
            {
                await InvokeHandlerSafelyAsync(
                    handler, h => h.OnTaskCompletedAsync(context, cancellationToken),
                    nameof(ICallbackHandler.OnTaskCompletedAsync), cancellationToken).ConfigureAwait(false);
            }

            // Dispatch to optional per-call handlers
            if (handlers?.AgentCallbackHandler != null)
            {
                await InvokeHandlerSafelyAsync(
                    handlers.AgentCallbackHandler, h => h.OnTaskCompletedAsync(context, cancellationToken),
                    nameof(ICallbackHandler.OnTaskCompletedAsync), cancellationToken).ConfigureAwait(false);
            }

            if (handlers?.TaskCallbackHandler != null)
            {
                await InvokeHandlerSafelyAsync(
                    handlers.TaskCallbackHandler, h => h.OnTaskCompletedAsync(context, cancellationToken),
                    nameof(ICallbackHandler.OnTaskCompletedAsync), cancellationToken).ConfigureAwait(false);
            }

            // Dispatch to TaskCallbacks
            if (handlers?.TaskCallbacks != null)
            {
                if (completionInfo.Result.Success && handlers.TaskCallbacks.OnCompleted != null)
                {
                    await InvokeDelegateSafelyAsync(
                        () => handlers.TaskCallbacks.OnCompleted(context),
                        "TaskCallbacks.OnCompleted", cancellationToken).ConfigureAwait(false);
                }
                else if (!completionInfo.Result.Success && handlers.TaskCallbacks.OnFailed != null)
                {
                    await InvokeDelegateSafelyAsync(
                        () => handlers.TaskCallbacks.OnFailed(context),
                        "TaskCallbacks.OnFailed", cancellationToken).ConfigureAwait(false);
                }

                if (handlers.TaskCallbacks.OnFinally != null)
                {
                    await InvokeDelegateSafelyAsync(
                        () => handlers.TaskCallbacks.OnFinally(context),
                        "TaskCallbacks.OnFinally", cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Notify Step Progress Async.
    /// </summary>
    public System.Threading.Tasks.Task NotifyStepProgressAsync(
        DomainAgent agent,
        CrewTask task,
        StepProgressInfo progressInfo,
        CallbackHandlers? handlers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(progressInfo);

        return NotifyStepProgressCoreAsync();

        async System.Threading.Tasks.Task NotifyStepProgressCoreAsync()
        {
            LogStepProgress(task.Id, progressInfo.CurrentStep, progressInfo.TotalSteps, progressInfo.StepDescription);

            var progressPercentage = progressInfo.TotalSteps > 0 ? (double)progressInfo.CurrentStep / progressInfo.TotalSteps * 100.0 : 0.0;
            var context = new TaskProgressContext(
                TaskId: task.Id.ToString(),
                AgentId: agent.Id.ToString(),
                StepNumber: progressInfo.CurrentStep,
                TotalSteps: progressInfo.TotalSteps,
                ProgressPercentage: progressPercentage,
                CurrentAction: progressInfo.StepDescription,
                Timestamp: DateTime.UtcNow);

            // Dispatch to all registered handlers
            foreach (var handler in _handlers)
            {
                await InvokeHandlerSafelyAsync(
                    handler, h => h.OnTaskProgressAsync(context, cancellationToken),
                    nameof(ICallbackHandler.OnTaskProgressAsync), cancellationToken).ConfigureAwait(false);
            }

            // Dispatch to optional per-call handlers
            if (handlers?.AgentCallbackHandler != null)
            {
                await InvokeHandlerSafelyAsync(
                    handlers.AgentCallbackHandler, h => h.OnTaskProgressAsync(context, cancellationToken),
                    nameof(ICallbackHandler.OnTaskProgressAsync), cancellationToken).ConfigureAwait(false);
            }

            if (handlers?.TaskCallbackHandler != null)
            {
                await InvokeHandlerSafelyAsync(
                    handlers.TaskCallbackHandler, h => h.OnTaskProgressAsync(context, cancellationToken),
                    nameof(ICallbackHandler.OnTaskProgressAsync), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Notify Tool Used Async.
    /// </summary>
    public System.Threading.Tasks.Task NotifyToolUsedAsync(
        DomainAgent agent,
        string toolName,
        object input,
        object output,
        TimeSpan duration,
        ICallbackHandler? agentCallbackHandler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);

        return NotifyToolUsedCoreAsync();

        async System.Threading.Tasks.Task NotifyToolUsedCoreAsync()
        {
            LogToolUsed(agent.Id, toolName, duration.TotalMilliseconds);

            // Tool usage maps to a step completed event
            var context = new StepCompletedContext(
                Step: new StepIdentity(agent.Id.ToString(), agent.Role.ToString(), string.Empty),
                Action: $"tool:{toolName}",
                Thought: $"Using tool {toolName}",
                Observation: output?.ToString() ?? string.Empty,
                Success: true,
                Duration: duration,
                Timestamp: DateTime.UtcNow);

            // Dispatch to all registered handlers
            foreach (var handler in _handlers)
            {
                await InvokeHandlerSafelyAsync(
                    handler, h => h.OnStepCompletedAsync(context, cancellationToken),
                    nameof(ICallbackHandler.OnStepCompletedAsync), cancellationToken).ConfigureAwait(false);
            }

            // Dispatch to optional handler
            if (agentCallbackHandler != null)
            {
                await InvokeHandlerSafelyAsync(
                    agentCallbackHandler, h => h.OnStepCompletedAsync(context, cancellationToken),
                    nameof(ICallbackHandler.OnStepCompletedAsync), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Notify Delegation Async.
    /// </summary>
    public System.Threading.Tasks.Task NotifyDelegationAsync(
        DomainAgent fromAgent,
        DomainAgent toAgent,
        CrewTask task,
        string reason,
        ICallbackHandler? agentCallbackHandler = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fromAgent);
        ArgumentNullException.ThrowIfNull(toAgent);
        ArgumentNullException.ThrowIfNull(task);

        return NotifyDelegationCoreAsync();

        async System.Threading.Tasks.Task NotifyDelegationCoreAsync()
        {
            LogDelegation(task.Id, fromAgent.Role, toAgent.Role, reason);

            // Delegation maps to a step completed event on the delegating agent
            var context = new StepCompletedContext(
                Step: new StepIdentity(fromAgent.Id.ToString(), fromAgent.Role.ToString(), task.Id.ToString()),
                Action: $"delegate_to:{toAgent.Role}",
                Thought: reason,
                Observation: $"Task delegated to {toAgent.Role}",
                Success: true,
                Duration: TimeSpan.Zero,
                Timestamp: DateTime.UtcNow);

            // Dispatch to all registered handlers
            foreach (var handler in _handlers)
            {
                await InvokeHandlerSafelyAsync(
                    handler, h => h.OnStepCompletedAsync(context, cancellationToken),
                    nameof(ICallbackHandler.OnStepCompletedAsync), cancellationToken).ConfigureAwait(false);
            }

            // Dispatch to optional handler
            if (agentCallbackHandler != null)
            {
                await InvokeHandlerSafelyAsync(
                    agentCallbackHandler, h => h.OnStepCompletedAsync(context, cancellationToken),
                    nameof(ICallbackHandler.OnStepCompletedAsync), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Safely invokes a callback handler method, catching and logging any exceptions.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Callback fault barrier: a faulty user callback handler is logged (cancellation handled separately) so one bad handler cannot break the execution callback dispatch.")]
    private async System.Threading.Tasks.Task InvokeHandlerSafelyAsync(
        ICallbackHandler handler,
        Func<ICallbackHandler, System.Threading.Tasks.Task> action,
        string methodName,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        try
        {
            await action(handler).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is expected, don't log as error
        }
        catch (Exception ex)
        {
            LogCallbackHandlerException(ex, handler.GetType().Name, methodName);
        }
    }

    /// <summary>
    /// Safely invokes a delegate callback, catching and logging any exceptions.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Callback fault barrier: a faulty user callback delegate is logged (cancellation handled separately) so one bad delegate cannot break the execution callback dispatch.")]
    private async System.Threading.Tasks.Task InvokeDelegateSafelyAsync(
        Func<System.Threading.Tasks.Task> action,
        string callbackName,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is expected, don't log as error
        }
        catch (Exception ex)
        {
            LogCallbackDelegateException(ex, callbackName);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Task {TaskId} started by agent {AgentId} at {StartTime}")]
    private partial void LogTaskStartedByAgent(object taskId, object agentId, DateTime startTime);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Task {TaskId} completed by agent {AgentId} with result: {Success}")]
    private partial void LogTaskCompletedByAgent(object taskId, object agentId, bool success);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Task {TaskId} step {CurrentStep}/{TotalSteps}: {Description}")]
    private partial void LogStepProgress(object taskId, int currentStep, int totalSteps, string description);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent {AgentId} used tool {ToolName} for {Duration}ms")]
    private partial void LogToolUsed(object agentId, string toolName, double duration);

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {TaskId} delegated from {FromAgent} to {ToAgent}: {Reason}")]
    private partial void LogDelegation(object taskId, object fromAgent, object toAgent, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Callback handler {HandlerType}.{Method} threw an exception. Continuing execution.")]
    private partial void LogCallbackHandlerException(Exception ex, string handlerType, string method);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Callback delegate {CallbackName} threw an exception. Continuing execution.")]
    private partial void LogCallbackDelegateException(Exception ex, string callbackName);
}
