using Orkeon.Domain.Common;

namespace Orkeon.Domain.Task;

/// <summary>
/// The order a crew runs its tasks in when no plan decides it: a <b>stable topological sort</b>
/// of the tasks on their declared dependencies. A task is placed only after every task it
/// depends on; among the tasks whose dependencies are all satisfied, the declared order is
/// kept — so a crew that declares no dependency runs exactly in the order it was written.
/// <para>
/// The declared order is not always the intended one: the multi-file YAML layout lists the
/// tasks in the ordinal order of their file names, so <c>consolidate.yaml</c> comes before
/// <c>extract.yaml</c> even when it declares <c>dependencies: [extract]</c>. Sorting on the
/// dependencies is what makes the order the same whichever layout the crew was written in.
/// </para>
/// <para>
/// Tolerant by design, never throwing: a dependency naming a task the list does not carry is
/// ignored (the crew cannot wait for something it will never run), and a cycle keeps the
/// declared order for the tasks caught in it — the first of them in declared order is placed
/// although a dependency is still pending, which unblocks the rest. The tasks placed that way
/// are reported in <see cref="TaskExecutionOrderResult{TTask}.ForcedTaskIds"/> so the caller
/// can log the warning it deserves.
/// </para>
/// </summary>
public static class TaskExecutionOrder
{
    /// <summary>
    /// Sorts <paramref name="tasks"/> so that every task comes after the tasks it depends on,
    /// keeping the declared order wherever the dependencies allow it.
    /// </summary>
    /// <typeparam name="TTask">The task type; anything exposing <see cref="ICrewTask.Dependencies"/>.</typeparam>
    /// <param name="tasks">The tasks in their declared order.</param>
    /// <returns>The ordered tasks, plus the ids of the tasks a cycle forced into place.</returns>
    public static TaskExecutionOrderResult<TTask> Resolve<TTask>(IReadOnlyList<TTask> tasks)
        where TTask : ICrewTask
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var present = new HashSet<TaskId>();
        foreach (var task in tasks)
            present.Add(task.TaskId);

        var remaining = new List<TTask>(tasks);
        var placed = new HashSet<TaskId>();
        var ordered = new List<TTask>(tasks.Count);
        var forced = new List<TaskId>();

        while (remaining.Count > 0)
        {
            var index = remaining.FindIndex(task => IsReady(task, present, placed));
            if (index < 0)
            {
                // Every remaining task waits on another remaining task: a cycle. Break it at
                // the first task in declared order rather than refusing the whole crew.
                index = 0;
                forced.Add(remaining[0].TaskId);
            }

            var next = remaining[index];
            remaining.RemoveAt(index);
            ordered.Add(next);
            placed.Add(next.TaskId);
        }

        return new TaskExecutionOrderResult<TTask>(ordered, forced);
    }

    /// <summary>A task is ready once each dependency the crew carries has been placed.</summary>
    private static bool IsReady<TTask>(TTask task, HashSet<TaskId> present, HashSet<TaskId> placed)
        where TTask : ICrewTask
    {
        foreach (var dependency in task.Dependencies)
        {
            if (present.Contains(dependency) && !placed.Contains(dependency))
                return false;
        }

        return true;
    }
}

/// <summary>Outcome of <see cref="TaskExecutionOrder.Resolve{TTask}"/>.</summary>
/// <typeparam name="TTask">The task type that was sorted.</typeparam>
public sealed class TaskExecutionOrderResult<TTask>
    where TTask : ICrewTask
{
    internal TaskExecutionOrderResult(IReadOnlyList<TTask> tasks, IReadOnlyList<TaskId> forcedTaskIds)
    {
        Tasks = tasks;
        ForcedTaskIds = forcedTaskIds;
    }

    /// <summary>The tasks in execution order.</summary>
    public IReadOnlyList<TTask> Tasks { get; }

    /// <summary>
    /// The tasks placed while one of their dependencies was still pending — each one names a
    /// circular dependency the declared order was kept for. Empty when the dependencies form
    /// no cycle.
    /// </summary>
    public IReadOnlyList<TaskId> ForcedTaskIds { get; }

    /// <summary>True when at least one cycle was broken.</summary>
    public bool HasCycle => ForcedTaskIds.Count > 0;
}
