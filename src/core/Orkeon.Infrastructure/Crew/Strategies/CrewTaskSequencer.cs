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
///   <item><description>A plan that carries tasks decides: its order is taken as is (the
///   planner validated it).</description></item>
///   <item><description>Otherwise the crew's own tasks run in a stable topological order on
///   their declared dependencies (<see cref="TaskExecutionOrder"/>): a task runs after every
///   task it depends on, and the declared order is kept wherever the dependencies allow it.
///   Falling back on the declared order alone ran the multi-file layout in the ordinal order
///   of its file names, so <c>consolidate.yaml</c> ran before the <c>extract.yaml</c> it
///   depends on.</description></item>
/// </list>
/// A task id the repository cannot resolve keeps its place at the end of the sequence, so
/// the strategy still logs and skips it the way it always did. A cycle never fails the crew:
/// the declared order is kept for the tasks caught in it and a warning names them.
/// </summary>
internal static partial class CrewTaskSequencer
{
    /// <summary>
    /// The task ids to run, in execution order: the plan's when it carries any, the crew's own
    /// tasks sorted on their dependencies otherwise.
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

        var planned = plan?.GetTasksInOrder().Select(pt => pt.TaskId).ToList();
        if (planned is { Count: > 0 })
            return planned;

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

        var order = TaskExecutionOrder.Resolve(declared);
        if (order.HasCycle)
        {
            LogCircularDependencies(
                logger,
                crew.Id,
                string.Join(", ", order.ForcedTaskIds.Select(id => id.ToString())));
        }

        var sequence = new List<TaskId>(crew.Tasks.Count);
        sequence.AddRange(order.Tasks.Select(task => task.Id));
        sequence.AddRange(unresolved);
        return sequence;
    }

    [LoggerMessage(
        EventId = 9420,
        Level = LogLevel.Warning,
        Message = "Crew {CrewId} declares circular task dependencies; the declared order is kept for the tasks caught in the cycle: {TaskIds}")]
    private static partial void LogCircularDependencies(ILogger logger, CrewId crewId, string taskIds);
}
