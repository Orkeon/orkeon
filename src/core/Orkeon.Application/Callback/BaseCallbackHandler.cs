namespace Orkeon.Application.Callback;

/// <summary>
/// Base implementation of ICallbackHandler with virtual methods.
/// Allows selective override of specific callback types.
/// </summary>
public abstract class BaseCallbackHandler : ICallbackHandler
{
    /// <inheritdoc />
    public virtual System.Threading.Tasks.Task OnStepStartedAsync(StepStartedContext context, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual System.Threading.Tasks.Task OnStepCompletedAsync(StepCompletedContext context, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual System.Threading.Tasks.Task OnTaskProgressAsync(TaskProgressContext context, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual System.Threading.Tasks.Task OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual System.Threading.Tasks.Task OnFlowStepStartedAsync(FlowStepStartedContext context, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual System.Threading.Tasks.Task OnFlowStepCompletedAsync(FlowStepCompletedContext context, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }
}


/// <summary>
/// Composite callback handler that delegates to multiple handlers.
/// </summary>
public class CompositeCallbackHandler : ICallbackHandler
{
    private readonly IReadOnlyList<ICallbackHandler> _handlers;

    /// <summary>Initializes a new instance of <see cref="CompositeCallbackHandler"/> with the given handlers.</summary>
    /// <param name="handlers">The callback handlers to delegate to.</param>
    public CompositeCallbackHandler(params ICallbackHandler[] handlers)
    {
        _handlers = handlers;
    }

    /// <summary>Initializes a new instance of <see cref="CompositeCallbackHandler"/> with the given handlers.</summary>
    /// <param name="handlers">The callback handlers to delegate to.</param>
    public CompositeCallbackHandler(IEnumerable<ICallbackHandler> handlers)
    {
        _handlers = handlers.ToList();
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task OnStepStartedAsync(StepStartedContext context, CancellationToken cancellationToken = default)
    {
        foreach (var handler in _handlers)
        {
            await handler.OnStepStartedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task OnStepCompletedAsync(StepCompletedContext context, CancellationToken cancellationToken = default)
    {
        foreach (var handler in _handlers)
        {
            await handler.OnStepCompletedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default)
    {
        foreach (var handler in _handlers)
        {
            await handler.OnTaskStartedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task OnTaskProgressAsync(TaskProgressContext context, CancellationToken cancellationToken = default)
    {
        foreach (var handler in _handlers)
        {
            await handler.OnTaskProgressAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
    {
        foreach (var handler in _handlers)
        {
            await handler.OnTaskCompletedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task OnFlowStepStartedAsync(FlowStepStartedContext context, CancellationToken cancellationToken = default)
    {
        foreach (var handler in _handlers)
        {
            await handler.OnFlowStepStartedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task OnFlowStepCompletedAsync(FlowStepCompletedContext context, CancellationToken cancellationToken = default)
    {
        foreach (var handler in _handlers)
        {
            await handler.OnFlowStepCompletedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
