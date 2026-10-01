using Orkeon.Application.Callback;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="ICallbackHandler"/> that records every hook it receives, in order,
/// as <c>Kind:detail</c> lines, and keeps the step contexts for closer inspection.
/// </summary>
internal sealed class RecordingCallbackHandler : ICallbackHandler
{
    private readonly object _lock = new();

    public List<string> Hooks { get; } = [];

    public List<StepStartedContext> StepsStarted { get; } = [];

    public List<StepCompletedContext> StepsCompleted { get; } = [];

    public System.Threading.Tasks.Task OnStepStartedAsync(StepStartedContext context, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Hooks.Add($"StepStarted:{context.Action}");
            StepsStarted.Add(context);
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task OnStepCompletedAsync(StepCompletedContext context, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Hooks.Add($"StepCompleted:{context.Action}:{context.Success}");
            StepsCompleted.Add(context);
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default)
    {
        lock (_lock) { Hooks.Add($"TaskStarted:{context.TaskId}"); }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
    {
        lock (_lock) { Hooks.Add($"TaskCompleted:{context.TaskId}:{context.Success}"); }
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
