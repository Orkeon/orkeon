using Microsoft.Extensions.Logging;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// The order a strategy runs a crew's tasks in — one answer for every mode that runs them
/// one after another (STUDIO-12 C2), and the order the crew's planner numbers them in (GAP-31).
/// <list type="bullet">
///   <item><description>The tasks run in a stable topological order on their declared
///   dependencies (<see cref="TaskExecutionOrder"/>): a task runs after every task it depends
///   on, and the declared order is kept wherever the dependencies allow it. Falling back on the
///   declared order alone ran the multi-file layout in the ordinal order of its file names, so
///   <c>consolidate.yaml</c> ran before the <c>extract.yaml</c> it depends on.</description></item>
///   <item><description>A plan never changes it (GAP-31): with or without <c>planning: true</c>, a crew
///   runs the same tasks in the same order. Following the plan's order, even under the dependencies
///   (GAP-29), left the crew's output, the context a task reads and a task run twice at the mercy of
///   a model.</description></item>
/// </list>
/// A task id the repository cannot resolve keeps its place at the end of the sequence, so
/// the strategy still logs and skips it the way it always did. A cycle never fails the crew:
/// the declared order is kept for the tasks caught in it and a warning names them.
/// </summary>
internal static partial class CrewTaskSequencer
{
    /// <summary>
    /// The task ids to run, in execution order: the crew's tasks sorted on their dependencies, in
    /// the declared order wherever the dependencies allow it.
    /// </summary>
    /// <param name="crew">The crew whose tasks run.</param>
    /// <param name="tasks">Where the crew's tasks are loaded from, for their dependencies.</param>
    /// <param name="logger">Receives the cycle warning, when there is one.</param>
    /// <param name="cancellationToken">Cancels the loading.</param>
    internal static async Task<IReadOnlyList<TaskId>> ResolveAsync(
        DomainCrew crew,
        ITaskRepository tasks,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(logger);

        var (order, unresolved) = await SortAsync(crew, tasks, cancellationToken).ConfigureAwait(false);
        if (order.HasCycle)
        {
            LogCircularDependencies(
                logger,
                crew.Id,
                string.Join(", ", order.ForcedTaskIds.Select(id => id.ToString())));
        }

        var ordered = new List<TaskId>(order.Tasks.Count + unresolved.Count);
        ordered.AddRange(order.Tasks.Select(task => task.Id));
        ordered.AddRange(unresolved);
        return ordered;
    }

    /// <summary>
    /// The crew's tasks the repository resolves, in the order a run takes them — the order the crew's
    /// planner numbers them in (GAP-31). Says nothing of a cycle: the strategy that runs them does.
    /// </summary>
    /// <param name="crew">The crew whose tasks run.</param>
    /// <param name="tasks">Where the crew's tasks are loaded from.</param>
    /// <param name="cancellationToken">Cancels the loading.</param>
    internal static async Task<IReadOnlyList<CrewTask>> InRunOrderAsync(
        DomainCrew crew,
        ITaskRepository tasks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(tasks);

        var (order, _) = await SortAsync(crew, tasks, cancellationToken).ConfigureAwait(false);
        return order.Tasks;
    }

    private static async Task<(TaskExecutionOrderResult<CrewTask> Order, List<TaskId> Unresolved)> SortAsync(
        DomainCrew crew,
        ITaskRepository tasks,
        CancellationToken cancellationToken)
    {
        var declared = new List<CrewTask>(crew.Tasks.Count);
        var unresolved = new List<TaskId>();
        foreach (var taskId in crew.Tasks)
        {
            var task = await tasks.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
            if (task is null)
                unresolved.Add(taskId);
            else
                declared.Add(task);
        }

        return (TaskExecutionOrder.Resolve(declared), unresolved);
    }

    [LoggerMessage(
        EventId = 9420,
        Level = LogLevel.Warning,
        Message = "Crew {CrewId} declares circular task dependencies; the declared order is kept for the tasks caught in the cycle: {TaskIds}")]
    private static partial void LogCircularDependencies(ILogger logger, CrewId crewId, string taskIds);
}
