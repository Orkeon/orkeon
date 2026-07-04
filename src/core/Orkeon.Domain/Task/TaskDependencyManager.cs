using Orkeon.Domain.Common;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Domain.Task;

/// <summary>
/// Manages task dependencies: add, remove, and completion checks.
/// Extracted from Task to follow Single Responsibility Principle.
/// This is a domain helper (POCO), not a service — no dependency injection.
/// </summary>
internal sealed class TaskDependencyManager
{
    private readonly List<TaskId> _dependencies;

    public TaskDependencyManager(List<TaskId> dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _dependencies = dependencies;
    }

    /// <summary>
    /// Gets the dependencies as a read-only list.
    /// </summary>
    public IReadOnlyList<TaskId> Dependencies => _dependencies.AsReadOnly();

    /// <summary>
    /// Adds a dependency. Validates that the task does not depend on itself,
    /// is not a duplicate, and the task has not already started.
    /// </summary>
    /// <returns>True if the dependency was actually added; false if it was already present.</returns>
    public bool AddDependency(TaskId dependencyId, TaskId ownTaskId, TaskStatus status)
    {
        ArgumentNullException.ThrowIfNull(dependencyId);

        if (dependencyId == ownTaskId)
            throw new InvalidOperationException("A task cannot depend on itself.");

        if (_dependencies.Contains(dependencyId))
            return false;

        if (status != TaskStatus.Pending)
            throw new InvalidOperationException("Cannot add dependencies to a task that has started.");

        _dependencies.Add(dependencyId);
        return true;
    }

    /// <summary>
    /// Removes a dependency. Validates that the task has not already started.
    /// </summary>
    /// <returns>True if the dependency was actually removed; false if it was not present.</returns>
    public bool RemoveDependency(TaskId dependencyId, TaskStatus status)
    {
        ArgumentNullException.ThrowIfNull(dependencyId);

        if (!_dependencies.Contains(dependencyId))
            return false;

        if (status != TaskStatus.Pending)
            throw new InvalidOperationException("Cannot remove dependencies from a task that has started.");

        _dependencies.Remove(dependencyId);
        return true;
    }

    /// <summary>
    /// Checks if this task has uncompleted dependencies.
    /// </summary>
    public bool HasUncompletedDependencies(Func<TaskId, bool>? isTaskCompleted = null)
    {
        if (_dependencies.Count == 0)
            return false;

        if (isTaskCompleted == null)
            return true; // Conservative: assume dependencies are not completed

        return _dependencies.Any(dep => !isTaskCompleted(dep));
    }
}
