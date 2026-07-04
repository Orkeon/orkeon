using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Task;

/// <summary>
/// Interface for callbacks during task execution.
/// </summary>
public interface ITaskCallback
{
    /// <summary>
    /// Called when a task is about to start.
    /// </summary>
    System.Threading.Tasks.Task OnTaskStartAsync(ICrewTask task);

    /// <summary>
    /// Called when a task completes successfully.
    /// </summary>
    System.Threading.Tasks.Task OnTaskCompletedAsync(ICrewTask task, TaskOutput output);

    /// <summary>
    /// Called when a task fails.
    /// </summary>
    System.Threading.Tasks.Task OnTaskFailedAsync(ICrewTask task, string errorMessage);
}
