using System.Diagnostics;
using Orkeon.Application.Callback;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Constants.Protocol;

namespace Orkeon.Application.Execution;

/// <summary>
/// Wraps the tool-invocation point of the agent loops so that every tool call is an agent step
/// for the registered <see cref="ICallbackHandler"/>s: <see cref="ICallbackHandler.OnStepStartedAsync"/>
/// before the call, <see cref="ICallbackHandler.OnStepCompletedAsync"/> after it — on success,
/// on a tool failure, on a guardian block and when the tool throws (the exception is rethrown).
/// When the run is streamed (<see cref="CrewStreamScope"/>, GAP-32), the call is also a
/// <c>tool.called</c> event — the tool's name, never the arguments' values — and its end a
/// <c>tool.returned</c> event — success and duration, not the result —, the exception included.
/// </summary>
/// <remarks>
/// A decorator rather than a field of <see cref="IToolInvocationPipeline"/>'s default
/// implementation: the pipeline is a singleton, the callback orchestrator and its handlers are
/// scoped to the run, and whether a run is streamed is known only when the call is made.
/// </remarks>
public sealed class StepNotifyingToolInvocationPipeline : IToolInvocationPipeline
{
    private readonly IToolInvocationPipeline _inner;
    private readonly ICallbackOrchestrator? _callbacks;

    private StepNotifyingToolInvocationPipeline(IToolInvocationPipeline inner, ICallbackOrchestrator? callbacks)
    {
        _inner = inner;
        _callbacks = callbacks;
    }

    /// <summary>
    /// Returns <paramref name="inner"/> wrapped so its calls notify <paramref name="callbacks"/> and
    /// the stream of a streamed run — wrapped even without callbacks, since a run decides at call
    /// time whether it is streamed —, or <paramref name="inner"/> itself when it is already wrapped.
    /// </summary>
    /// <param name="inner">The tool-invocation point to wrap.</param>
    /// <param name="callbacks">The callback orchestrator of the run; null notifies the stream alone.</param>
    public static IToolInvocationPipeline Wrap(IToolInvocationPipeline inner, ICallbackOrchestrator? callbacks)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return inner is StepNotifyingToolInvocationPipeline
            ? inner
            : new StepNotifyingToolInvocationPipeline(inner, callbacks);
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<ToolInvocationResult> InvokeAsync(
        ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return InvokeCoreAsync(invocation, cancellationToken);
    }

    private async System.Threading.Tasks.Task<ToolInvocationResult> InvokeCoreAsync(
        ToolInvocation invocation, CancellationToken cancellationToken)
    {
        var caller = invocation.Caller;
        var step = new StepIdentity(caller.AgentId, caller.AgentRole, caller.TaskId ?? string.Empty);
        var action = $"tool:{invocation.Tool.Name}";
        var thought = $"Using tool {invocation.Tool.Name}";

        // The task and the agent of the call, as the token meter knows them (LlmUsageScope): a
        // delegated or a scripted call that names no task still belongs to the one being run.
        var usage = LlmUsageScope.Current;
        var taskId = NullIfEmpty(caller.TaskId) ?? NullIfEmpty(usage.TaskId);
        var agentRole = NullIfEmpty(caller.AgentRole) ?? NullIfEmpty(usage.AgentId);
        CrewStreamScope.Write(new CrewExecutionEvent
        {
            Kind = RunEventKinds.ToolCalled,
            TaskId = taskId,
            AgentRole = agentRole,
            ToolName = invocation.Tool.Name,
        });

        if (_callbacks is not null)
        {
            await _callbacks.NotifyStepStartedAsync(
                new StepStartedContext(step.AgentId, step.AgentRole, step.TaskId, action, thought, DateTime.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }

        var stopwatch = Stopwatch.StartNew();
        ToolInvocationResult result;
        try
        {
            result = await _inner.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The stream hears every end, a cancellation included: a watcher never shows a call
            // running forever. The callbacks keep their rule — a cancellation is not a step result.
            WriteReturned(invocation, taskId, agentRole, success: false, stopwatch.Elapsed);
            if (_callbacks is not null && ex is not OperationCanceledException)
            {
                await _callbacks.NotifyStepCompletedAsync(
                    new StepCompletedContext(step, action, thought, $"Error: {ex.Message}", false, stopwatch.Elapsed, DateTime.UtcNow),
                    cancellationToken).ConfigureAwait(false);
            }

            throw;
        }

        WriteReturned(invocation, taskId, agentRole, result.Success, stopwatch.Elapsed);
        if (_callbacks is not null)
        {
            await _callbacks.NotifyStepCompletedAsync(
                new StepCompletedContext(step, action, thought, result.RawText, result.Success, stopwatch.Elapsed, DateTime.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private static void WriteReturned(ToolInvocation invocation, string? taskId, string? agentRole, bool success, TimeSpan duration) =>
        CrewStreamScope.Write(new CrewExecutionEvent
        {
            Kind = RunEventKinds.ToolReturned,
            TaskId = taskId,
            AgentRole = agentRole,
            ToolName = invocation.Tool.Name,
            Success = success,
            Duration = duration,
        });

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
