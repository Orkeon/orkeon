// Domain type aliases are defined in GlobalUsings.cs
namespace Orkeon.Application.Callback;

/// <summary>
/// Specific callback functions for task events.
/// Provides a more granular alternative to ICallbackHandler.
/// </summary>
public class TaskCallbacks
{
    /// <summary>
    /// Called before task execution starts.
    /// </summary>
    public Func<TaskStartedContext, System.Threading.Tasks.Task>? OnStarted { get; init; }

    /// <summary>
    /// Called during task execution to report progress.
    /// </summary>
    public Func<TaskProgressContext, System.Threading.Tasks.Task>? OnProgress { get; init; }

    /// <summary>
    /// Called after task completes successfully.
    /// </summary>
    public Func<TaskCompletedContext, System.Threading.Tasks.Task>? OnCompleted { get; init; }

    /// <summary>
    /// Called if task fails.
    /// </summary>
    public Func<TaskCompletedContext, System.Threading.Tasks.Task>? OnFailed { get; init; }

    /// <summary>
    /// Called for any completion (success or failure).
    /// </summary>
    public Func<TaskCompletedContext, System.Threading.Tasks.Task>? OnFinally { get; init; }

    /// <summary>
    /// Creates task callbacks with lambda functions.
    /// </summary>
    public static TaskCallbacks Create(
        Func<TaskStartedContext, System.Threading.Tasks.Task>? onStarted = null,
        Func<TaskProgressContext, System.Threading.Tasks.Task>? onProgress = null,
        Func<TaskCompletedContext, System.Threading.Tasks.Task>? onCompleted = null,
        Func<TaskCompletedContext, System.Threading.Tasks.Task>? onFailed = null,
        Func<TaskCompletedContext, System.Threading.Tasks.Task>? onFinally = null)
    {
        return new TaskCallbacks
        {
            OnStarted = onStarted,
            OnProgress = onProgress,
            OnCompleted = onCompleted,
            OnFailed = onFailed,
            OnFinally = onFinally
        };
    }

    /// <summary>
    /// Creates simple action-based callbacks.
    /// </summary>
    public static TaskCallbacks CreateActions(
        Action<TaskStartedContext>? onStarted = null,
        Action<TaskProgressContext>? onProgress = null,
        Action<TaskCompletedContext>? onCompleted = null,
        Action<TaskCompletedContext>? onFailed = null,
        Action<TaskCompletedContext>? onFinally = null)
    {
        return new TaskCallbacks
        {
            OnStarted = onStarted != null ? ctx => { onStarted(ctx); return System.Threading.Tasks.Task.CompletedTask; } : null,
            OnProgress = onProgress != null ? ctx => { onProgress(ctx); return System.Threading.Tasks.Task.CompletedTask; } : null,
            OnCompleted = onCompleted != null ? ctx => { onCompleted(ctx); return System.Threading.Tasks.Task.CompletedTask; } : null,
            OnFailed = onFailed != null ? ctx => { onFailed(ctx); return System.Threading.Tasks.Task.CompletedTask; } : null,
            OnFinally = onFinally != null ? ctx => { onFinally(ctx); return System.Threading.Tasks.Task.CompletedTask; } : null
        };
    }
}
