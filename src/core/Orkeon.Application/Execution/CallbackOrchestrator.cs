
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Callback;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Execution;

/// <summary>
/// Orchestrates callbacks for agent and task execution.
/// Dispatches to all registered ICallbackHandler instances.
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
        }
    }

    /// <summary>
    /// Notify Task Completed Async.
    /// </summary>
    public System.Threading.Tasks.Task NotifyTaskCompletedAsync(
        DomainAgent agent,
        CrewTask task,
        TaskCompletionInfo completionInfo,
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
        }
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task NotifyStepStartedAsync(
        StepStartedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        return NotifyStepStartedCoreAsync();

        async System.Threading.Tasks.Task NotifyStepStartedCoreAsync()
        {
            foreach (var handler in _handlers)
            {
                await InvokeHandlerSafelyAsync(
                    handler, h => h.OnStepStartedAsync(context, cancellationToken),
                    nameof(ICallbackHandler.OnStepStartedAsync), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task NotifyStepCompletedAsync(
        StepCompletedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        return NotifyStepCompletedCoreAsync();

        async System.Threading.Tasks.Task NotifyStepCompletedCoreAsync()
        {
            LogStepCompleted(context.AgentId, context.Action, context.Duration.TotalMilliseconds, context.Success);

            foreach (var handler in _handlers)
            {
                await InvokeHandlerSafelyAsync(
                    handler, h => h.OnStepCompletedAsync(context, cancellationToken),
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Task {TaskId} started by agent {AgentId} at {StartTime}")]
    private partial void LogTaskStartedByAgent(object taskId, object agentId, DateTime startTime);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Task {TaskId} completed by agent {AgentId} with result: {Success}")]
    private partial void LogTaskCompletedByAgent(object taskId, object agentId, bool success);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent {AgentId} step {Action} ended after {Duration}ms (success: {Success})")]
    private partial void LogStepCompleted(string agentId, string action, double duration, bool success);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Callback handler {HandlerType}.{Method} threw an exception. Continuing execution.")]
    private partial void LogCallbackHandlerException(Exception ex, string handlerType, string method);

}
