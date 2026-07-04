using Orkeon.Domain.Common;

namespace Orkeon.Domain.Crew;

/// <summary>
/// Represents an execution plan for crew tasks.
/// </summary>
public sealed record ExecutionPlan
{
    private readonly List<PlannedTask> _tasks;
    private readonly Dictionary<TaskId, AgentId> _assignments;

    /// <summary>
    /// Gets the planned tasks.
    /// </summary>
    public IReadOnlyList<PlannedTask> Tasks => _tasks.AsReadOnly();

    /// <summary>
    /// Gets the task-to-agent assignments.
    /// </summary>
    public IReadOnlyDictionary<TaskId, AgentId> Assignments => _assignments.AsReadOnly();

    private ExecutionPlan(
        List<PlannedTask> tasks,
        Dictionary<TaskId, AgentId> assignments)
    {
        _tasks = tasks;
        _assignments = assignments;
    }

    /// <summary>
    /// Creates an execution plan from task IDs.
    /// </summary>
    public static ExecutionPlan Create(IEnumerable<TaskId> taskIds)
    {
        ArgumentNullException.ThrowIfNull(taskIds);

        var tasks = new List<PlannedTask>();
        int order = 0;
        foreach (var taskId in taskIds)
        {
            tasks.Add(PlannedTask.Create(taskId, order++));
        }

        return new ExecutionPlan(tasks, []);
    }

    /// <summary>
    /// Creates an empty execution plan.
    /// </summary>
    public static ExecutionPlan Create()
    {
        return new ExecutionPlan([], []);
    }

    /// <summary>
    /// Returns a new execution plan with the specified task added.
    /// </summary>
    public ExecutionPlan WithTask(PlannedTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var newTasks = _tasks.ToList();
        newTasks.Add(task);
        return new ExecutionPlan(newTasks, new Dictionary<TaskId, AgentId>(_assignments));
    }

    /// <summary>
    /// Returns a new execution plan with the specified agent assignment.
    /// </summary>
    public ExecutionPlan WithAgentAssignment(TaskId taskId, AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        ArgumentNullException.ThrowIfNull(agentId);

        var newAssignments = new Dictionary<TaskId, AgentId>(_assignments)
        {
            [taskId] = agentId
        };
        return new ExecutionPlan(_tasks.ToList(), newAssignments);
    }

    /// <summary>
    /// Gets the assigned agent for a task.
    /// </summary>
    public AgentId? GetAssignedAgent(TaskId taskId)
    {
        return _assignments.TryGetValue(taskId, out var agentId) ? agentId : null;
    }

    /// <summary>
    /// Gets tasks in execution order.
    /// </summary>
    public IEnumerable<PlannedTask> GetTasksInOrder()
    {
        return _tasks.OrderBy(t => t.ExecutionOrder);
    }

    /// <summary>
    /// Gets tasks that can be executed in parallel.
    /// </summary>
    public IEnumerable<IGrouping<int, PlannedTask>> GetParallelGroups()
    {
        return _tasks.GroupBy(t => t.ParallelGroup ?? t.ExecutionOrder);
    }

    /// <inheritdoc />
    public bool Equals(ExecutionPlan? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (_tasks.Count != other._tasks.Count) return false;
        if (_assignments.Count != other._assignments.Count) return false;

        for (int i = 0; i < _tasks.Count; i++)
        {
            if (!_tasks[i].Equals(other._tasks[i]))
                return false;
        }

        foreach (var kvp in _assignments)
        {
            if (!other._assignments.TryGetValue(kvp.Key, out var otherValue) || kvp.Value != otherValue)
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var task in _tasks)
            hash.Add(task);
        foreach (var assignment in _assignments)
        {
            hash.Add(assignment.Key);
            hash.Add(assignment.Value);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Represents a task in the execution plan.
/// </summary>
public sealed record PlannedTask
{
    /// <summary>
    /// Gets the task ID.
    /// </summary>
    public TaskId TaskId { get; init; }

    /// <summary>
    /// Gets the execution order.
    /// </summary>
    public int ExecutionOrder { get; init; }

    /// <summary>
    /// Gets the parallel execution group.
    /// </summary>
    public int? ParallelGroup { get; init; }

    /// <summary>
    /// Gets the dependencies for this task.
    /// </summary>
    public IReadOnlyList<TaskId> Dependencies { get; init; }

    /// <summary>
    /// Gets any special instructions for this task.
    /// </summary>
    public string? Instructions { get; init; }

    private PlannedTask(
        TaskId taskId,
        int executionOrder,
        int? parallelGroup,
        IReadOnlyList<TaskId> dependencies,
        string? instructions)
    {
        TaskId = taskId;
        ExecutionOrder = executionOrder;
        ParallelGroup = parallelGroup;
        Dependencies = dependencies;
        Instructions = instructions;
    }

    /// <summary>
    /// Creates a new planned task.
    /// </summary>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="executionOrder">The execution order index.</param>
    /// <param name="parallelGroup">The parallel group number, or <see langword="null"/> if sequential.</param>
    /// <param name="dependencies">Optional task dependencies.</param>
    /// <param name="instructions">Optional special instructions.</param>
    public static PlannedTask Create(
        TaskId taskId,
        int executionOrder,
        int? parallelGroup = null,
        IEnumerable<TaskId>? dependencies = null,
        string? instructions = null)
    {
        ArgumentNullException.ThrowIfNull(taskId);

        return new PlannedTask(
            taskId,
            executionOrder,
            parallelGroup,
            dependencies?.ToList().AsReadOnly() ?? new List<TaskId>().AsReadOnly(),
            instructions);
    }

    /// <inheritdoc />
    public bool Equals(PlannedTask? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return TaskId == other.TaskId
            && ExecutionOrder == other.ExecutionOrder
            && ParallelGroup == other.ParallelGroup
            && Instructions == other.Instructions
            && Dependencies.SequenceEqual(other.Dependencies);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TaskId);
        hash.Add(ExecutionOrder);
        hash.Add(ParallelGroup);
        hash.Add(Instructions);
        foreach (var dep in Dependencies)
            hash.Add(dep);
        return hash.ToHashCode();
    }
}
