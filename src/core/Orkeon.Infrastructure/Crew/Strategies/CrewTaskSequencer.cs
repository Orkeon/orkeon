using Microsoft.Extensions.Logging;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using DomainCrew = Orkeon.Domain.Crew.Crew;
using DomainExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// The order a strategy runs a crew's tasks in — one answer for every mode that runs them
/// one after another (STUDIO-12 C2).
/// <list type="bullet">
///   <item><description>The tasks run in a stable topological order on their declared
///   dependencies (<see cref="TaskExecutionOrder"/>): a task runs after every task it depends
///   on. Falling back on the declared order alone ran the multi-file layout in the ordinal order
///   of its file names, so <c>consolidate.yaml</c> ran before the <c>extract.yaml</c> it
///   depends on.</description></item>
///   <item><description>Wherever the dependencies allow it, a plan that carries tasks decides
///   the order; without one, the declared order is kept. The planner sees task ids, not their
///   dependencies, so a plan taken as is could run a task ahead of one it depends on — the
///   defect above, brought back by <c>planning: true</c> (GAP-29).</description></item>
/// </list>
/// A task id the repository cannot resolve keeps its place at the end of the sequence, so
/// the strategy still logs and skips it the way it always did. A cycle never fails the crew:
/// the declared order is kept for the tasks caught in it and a warning names them.
/// </summary>
internal static partial class CrewTaskSequencer
{
    /// <summary>
    /// The task ids to run, in execution order: the crew's tasks sorted on their dependencies,
    /// in the plan's order wherever the dependencies allow it when the plan carries any, in the
    /// declared order otherwise.
    /// </summary>
    /// <param name="crew">The crew whose tasks run.</param>
    /// <param name="plan">The execution plan, or null for the modes that never receive one.</param>
    /// <param name="tasks">Where the crew's tasks are loaded from, for their dependencies.</param>
    /// <param name="logger">Receives the cycle warning, when there is one.</param>
    /// <param name="cancellationToken">Cancels the loading.</param>
    internal static async Task<IReadOnlyList<TaskId>> ResolveAsync(
        DomainCrew crew,
        DomainExecutionPlan? plan,
        ITaskRepository tasks,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(crew);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(logger);

        // The plan's tasks in the plan's order when it carries any, the crew's own in their
        // declared order otherwise — then sorted on the dependencies, stably.
        var planned = plan?.GetTasksInOrder().Select(pt => pt.TaskId).ToList();
        IReadOnlyList<TaskId> sequence = planned is { Count: > 0 } ? planned : crew.Tasks;

        var declared = new List<CrewTask>(sequence.Count);
        var unresolved = new List<TaskId>();
        foreach (var taskId in sequence)
        {
            var task = await tasks.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
            if (task is null)
                unresolved.Add(taskId);
            else
                declared.Add(task);
        }

        var order = TaskExecutionOrder.Resolve(declared);
        if (order.HasCycle)
        {
            LogCircularDependencies(
                logger,
                crew.Id,
                string.Join(", ", order.ForcedTaskIds.Select(id => id.ToString())));
        }

        var ordered = new List<TaskId>(sequence.Count);
        ordered.AddRange(order.Tasks.Select(task => task.Id));
        ordered.AddRange(unresolved);
        return ordered;
    }

    [LoggerMessage(
        EventId = 9420,
        Level = LogLevel.Warning,
        Message = "Crew {CrewId} declares circular task dependencies; the declared order is kept for the tasks caught in the cycle: {TaskIds}")]
    private static partial void LogCircularDependencies(ILogger logger, CrewId crewId, string taskIds);
}
