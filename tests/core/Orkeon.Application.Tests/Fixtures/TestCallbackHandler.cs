using Orkeon.Application.Callback;
namespace Orkeon.Application.Tests.Fixtures;

/// <summary>
/// Test double for ICallbackHandler used in unit tests.
/// </summary>
public class TestCallbackHandler : ICallbackHandler
{
    public List<StepStartedContext> StepStartedCalls { get; } = [];
    public List<StepCompletedContext> StepCompletedCalls { get; } = [];
    public List<TaskStartedContext> TaskStartedCalls { get; } = [];
    public List<TaskCompletedContext> TaskCompletedCalls { get; } = [];

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

    public System.Threading.Tasks.Task OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
    {
        TaskCompletedCalls.Add(context);
        if (ShouldThrowOnTaskCompleted && ExceptionToThrow != null)
        {
            throw ExceptionToThrow;
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public void Clear()
    {
        StepStartedCalls.Clear();
        StepCompletedCalls.Clear();
        TaskStartedCalls.Clear();
        TaskCompletedCalls.Clear();
        ShouldThrowOnTaskStarted = false;
        ShouldThrowOnTaskCompleted = false;
        ExceptionToThrow = null;
    }
}
