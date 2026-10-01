using System.Diagnostics;
using Orkeon.Application.Callback;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Application.Execution;

/// <summary>
/// Wraps the tool-invocation point of the agent loops so that every tool call is an agent step
/// for the registered <see cref="ICallbackHandler"/>s: <see cref="ICallbackHandler.OnStepStartedAsync"/>
/// before the call, <see cref="ICallbackHandler.OnStepCompletedAsync"/> after it — on success,
/// on a tool failure, on a guardian block and when the tool throws (the exception is rethrown).
/// </summary>
/// <remarks>
/// A decorator rather than a field of <see cref="IToolInvocationPipeline"/>'s default
/// implementation: the pipeline is a singleton, the callback orchestrator and its handlers are
/// scoped to the run.
/// </remarks>
public sealed class StepNotifyingToolInvocationPipeline : IToolInvocationPipeline
{
    private readonly IToolInvocationPipeline _inner;
    private readonly ICallbackOrchestrator _callbacks;

    private StepNotifyingToolInvocationPipeline(IToolInvocationPipeline inner, ICallbackOrchestrator callbacks)
    {
        _inner = inner;
        _callbacks = callbacks;
    }

    /// <summary>
    /// Returns <paramref name="inner"/> wrapped so its calls notify <paramref name="callbacks"/>,
    /// or <paramref name="inner"/> itself when there is nothing to notify.
    /// </summary>
    /// <param name="inner">The tool-invocation point to wrap.</param>
    /// <param name="callbacks">The callback orchestrator of the run; null leaves <paramref name="inner"/> unwrapped.</param>
    public static IToolInvocationPipeline Wrap(IToolInvocationPipeline inner, ICallbackOrchestrator? callbacks)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return callbacks is null || inner is StepNotifyingToolInvocationPipeline
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

        await _callbacks.NotifyStepStartedAsync(
            new StepStartedContext(step.AgentId, step.AgentRole, step.TaskId, action, thought, DateTime.UtcNow),
            cancellationToken).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        ToolInvocationResult result;
        try
        {
            result = await _inner.InvokeAsync(invocation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _callbacks.NotifyStepCompletedAsync(
                new StepCompletedContext(step, action, thought, $"Error: {ex.Message}", false, stopwatch.Elapsed, DateTime.UtcNow),
                cancellationToken).ConfigureAwait(false);
            throw;
        }

        await _callbacks.NotifyStepCompletedAsync(
            new StepCompletedContext(step, action, thought, result.RawText, result.Success, stopwatch.Elapsed, DateTime.UtcNow),
            cancellationToken).ConfigureAwait(false);

        return result;
    }
}
