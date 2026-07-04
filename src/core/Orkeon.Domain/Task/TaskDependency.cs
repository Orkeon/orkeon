using Orkeon.Domain.Common;

namespace Orkeon.Domain.Task;

/// <summary>
/// Represents a dependency between tasks.
/// </summary>
public class TaskDependency
{
    /// <summary>Gets the task identifier that has the dependency.</summary>
    public TaskId TaskId { get; private set; } = Common.TaskId.Create();
    /// <summary>Gets the identifier of the task this task depends on.</summary>
    public TaskId DependsOnTaskId { get; private set; } = Common.TaskId.Create();
    /// <summary>Gets the dependency type.</summary>
    public DependencyType Type { get; private set; } = DependencyType.Hard;
    /// <summary>Gets an optional condition expression for the dependency.</summary>
    public string? Condition { get; private set; }
    /// <summary>Gets the maximum time to wait for the dependency, or <see langword="null"/> for no limit.</summary>
    public TimeSpan? MaxWaitTime { get; private set; }

    /// <summary>Initializes a new empty instance of <see cref="TaskDependency"/>.</summary>
    internal TaskDependency()
    {
    }

    /// <summary>Initializes a new instance of <see cref="TaskDependency"/>.</summary>
    /// <param name="taskId">The dependent task identifier.</param>
    /// <param name="dependsOnTaskId">The task being depended upon.</param>
    /// <param name="type">The dependency type.</param>
    /// <param name="condition">An optional condition expression.</param>
    /// <param name="maxWaitTime">An optional maximum wait time.</param>
    internal TaskDependency(TaskId taskId, TaskId dependsOnTaskId, DependencyType type = DependencyType.Hard, string? condition = null, TimeSpan? maxWaitTime = null)
    {
        TaskId = taskId;
        DependsOnTaskId = dependsOnTaskId;
        Type = type;
        Condition = condition;
        MaxWaitTime = maxWaitTime;
    }
}

/// <summary>
/// Types of task dependencies.
/// </summary>
public enum DependencyType
{
    /// <summary>
    /// Task cannot start until dependency is completed.
    /// </summary>
    Hard,

    /// <summary>
    /// Task can start but may use output from dependency if available.
    /// </summary>
    Soft,

    /// <summary>
    /// Task should wait for dependency but can proceed after timeout.
    /// </summary>
    Timed
}
