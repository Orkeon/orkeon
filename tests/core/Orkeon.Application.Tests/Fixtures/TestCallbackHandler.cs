using Orkeon.Application.Callback;
using Orkeon.Domain.Agent;
namespace Orkeon.Application.Tests.Fixtures;

/// <summary>
/// Test double for ICallbackHandler used in unit tests.
/// </summary>
public class TestCallbackHandler : ICallbackHandler, IStepProgressHandler
{
    public List<StepStartedContext> StepStartedCalls { get; } = [];
    public List<StepCompletedContext> StepCompletedCalls { get; } = [];
    public List<TaskStartedContext> TaskStartedCalls { get; } = [];
    public List<TaskProgressContext> TaskProgressCalls { get; } = [];
    public List<TaskCompletedContext> TaskCompletedCalls { get; } = [];
    public List<FlowStepStartedContext> FlowStepStartedCalls { get; } = [];
    public List<FlowStepCompletedContext> FlowStepCompletedCalls { get; } = [];

    // For IStepProgressHandler
    public List<StepProgressContext> StepProgressCalls { get; } = [];

    public bool ShouldThrowOnTaskStarted { get; set; }
    public bool ShouldThrowOnTaskCompleted { get; set; }
    public Exception? ExceptionToThrow { get; set; }

    public System.Threading.Tasks.Task OnStepStartedAsync(StepStartedContext context, CancellationToken cancellationToken = default)
    {
        StepStartedCalls.Add(context);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task OnStepCompletedAsync(StepCompletedContext context, CancellationToken cancellationToken = default)
    {
        StepCompletedCalls.Add(context);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default)
    {
        TaskStartedCalls.Add(context);
        if (ShouldThrowOnTaskStarted && ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task OnTaskProgressAsync(TaskProgressContext context, CancellationToken cancellationToken = default)
    {
        TaskProgressCalls.Add(context);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
    {
        TaskCompletedCalls.Add(context);
        if (ShouldThrowOnTaskCompleted && ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task OnFlowStepStartedAsync(FlowStepStartedContext context, CancellationToken cancellationToken = default)
    {
        FlowStepStartedCalls.Add(context);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task OnFlowStepCompletedAsync(FlowStepCompletedContext context, CancellationToken cancellationToken = default)
    {
        FlowStepCompletedCalls.Add(context);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    // IStepProgressHandler method
    public System.Threading.Tasks.Task OnStepProgressAsync(StepProgressContext context, CancellationToken cancellationToken = default)
    {
        StepProgressCalls.Add(context);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public void Clear()
    {
        StepStartedCalls.Clear();
        StepCompletedCalls.Clear();
        TaskStartedCalls.Clear();
        TaskProgressCalls.Clear();
        TaskCompletedCalls.Clear();
        FlowStepStartedCalls.Clear();
        FlowStepCompletedCalls.Clear();
        StepProgressCalls.Clear();
        ShouldThrowOnTaskStarted = false;
        ShouldThrowOnTaskCompleted = false;
        ExceptionToThrow = null;
    }
}
