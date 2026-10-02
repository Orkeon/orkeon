using Orkeon.Domain.Common;

namespace Orkeon.Domain.Crew;

/// <summary>
/// The crew's plan (<c>planning: true</c>, GAP-31): one step-by-step plan per task, written by the
/// crew's planner before the run and read by each task in its prompt. That is all it holds. It
/// decides neither the order of the tasks nor who runs them — the crew does, with or without a plan
/// — so the order, agent assignments, parallel groups and dependencies a plan used to carry are gone.
/// </summary>
public sealed record ExecutionPlan
{
    private readonly IReadOnlyList<PlannedTask> _tasks;
    private readonly Dictionary<TaskId, string> _instructions;

    private ExecutionPlan(List<PlannedTask> tasks)
    {
        _tasks = tasks.AsReadOnly();
        _instructions = tasks.ToDictionary(task => task.TaskId, task => task.Instructions);
    }

    /// <summary>The plan of a run that planned nothing: no task has instructions.</summary>
    public static ExecutionPlan Empty { get; } = new([]);

    /// <summary>The plan of each task, in the order the planner wrote them.</summary>
    public IReadOnlyList<PlannedTask> Tasks => _tasks;

    /// <summary>Creates a plan from one plan per task.</summary>
    /// <param name="tasks">The plan of each task; a task appears once.</param>
    /// <exception cref="ArgumentException">A task appears twice.</exception>
    public static ExecutionPlan Create(IEnumerable<PlannedTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var planned = tasks.ToList();
        var seen = new HashSet<TaskId>();
        foreach (var task in planned)
        {
            ArgumentNullException.ThrowIfNull(task, nameof(tasks));
            if (!seen.Add(task.TaskId))
                throw new ArgumentException($"Task {task.TaskId} has two plans; a plan holds one per task.", nameof(tasks));
        }

        return planned.Count == 0 ? Empty : new ExecutionPlan(planned);
    }

    /// <summary>The plan the planner wrote for <paramref name="taskId"/>, or null when it wrote none.</summary>
    /// <param name="taskId">The task.</param>
    public string? InstructionsFor(TaskId taskId)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        return _instructions.TryGetValue(taskId, out var instructions) ? instructions : null;
    }

    /// <inheritdoc />
    public bool Equals(ExecutionPlan? other) =>
        other is not null && (ReferenceEquals(this, other) || _tasks.SequenceEqual(other._tasks));

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var task in _tasks)
            hash.Add(task);
        return hash.ToHashCode();
    }
}

/// <summary>The plan of one task: its step-by-step instructions, as the crew's planner wrote them.</summary>
public sealed record PlannedTask
{
    /// <summary>Creates the plan of one task.</summary>
    /// <param name="taskId">The task.</param>
    /// <param name="instructions">The steps the planner wrote for it.</param>
    public PlannedTask(TaskId taskId, string instructions)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        ArgumentNullException.ThrowIfNull(instructions);
        TaskId = taskId;
        Instructions = instructions;
    }

    /// <summary>The task.</summary>
    public TaskId TaskId { get; }

    /// <summary>The steps the planner wrote for the task.</summary>
    public string Instructions { get; }
}
