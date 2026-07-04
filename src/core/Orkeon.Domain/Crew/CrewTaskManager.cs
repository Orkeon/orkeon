using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.ValueObjects;

namespace Orkeon.Domain.Crew;

/// <summary>
/// Manages tasks within a Crew: add, remove, and query operations.
/// Extracted from Crew to follow Single Responsibility Principle.
/// This is a domain helper (POCO), not a service — no dependency injection.
/// </summary>
internal sealed class CrewTaskManager
{
    private readonly List<TaskId> _tasks;

    public CrewTaskManager(List<TaskId> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        _tasks = tasks;
    }

    /// <summary>
    /// Gets the tasks as a read-only list.
    /// </summary>
    public IReadOnlyList<TaskId> Tasks => _tasks.AsReadOnly();

    /// <summary>
    /// Adds a task to the crew. Validates that the task is not already present
    /// and that the crew is not currently executing.
    /// </summary>
    public void AddTask(TaskId taskId, CrewStatus status)
    {
        ArgumentNullException.ThrowIfNull(taskId);

        if (_tasks.Contains(taskId))
            throw new InvalidOperationException($"Task {taskId} is already in this crew.");

        if (status == CrewStatus.Executing)
            throw new InvalidOperationException("Cannot add tasks while crew is executing.");

        _tasks.Add(taskId);
    }

    /// <summary>
    /// Removes a task from the crew. Validates that the task exists
    /// and that the crew is not currently executing.
    /// </summary>
    public void RemoveTask(TaskId taskId, string reason, CrewStatus status)
    {
        ArgumentNullException.ThrowIfNull(taskId);

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!_tasks.Contains(taskId))
            throw new InvalidOperationException($"Task {taskId} is not in this crew.");

        if (status == CrewStatus.Executing)
            throw new InvalidOperationException("Cannot remove tasks while crew is executing.");

        _tasks.Remove(taskId);
    }

    /// <summary>
    /// Checks if the crew has any tasks.
    /// </summary>
    public bool Any() => _tasks.Count > 0;
}
