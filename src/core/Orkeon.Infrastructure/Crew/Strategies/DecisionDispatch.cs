using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using ApplicationTaskOutput = Orkeon.Application.Execution.TaskOutput;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>Runs one decision task through the decision runner, never through an agent.</summary>
internal sealed class DecisionDispatch
{
    /// <summary>The role a decision task reports under: it has no agent.</summary>
    internal const string Role = "decision";

    private readonly DecisionTaskRunner _runner;

    internal DecisionDispatch(DecisionTaskRunner runner) => _runner = runner;

    /// <summary>
    /// Runs <paramref name="task"/> over the outputs of the tasks it depends on, labelled by task name.
    /// </summary>
    /// <param name="task">The decision task.</param>
    /// <param name="crewTasks">The tasks of the crew, to name the dependencies.</param>
    /// <param name="previousOutputs">The outputs the run holds so far.</param>
    /// <param name="ct">The cancellation token.</param>
    internal async System.Threading.Tasks.Task<TaskResult> RunAsync(
        CrewTask task,
        IReadOnlyList<CrewTask> crewTasks,
        IReadOnlyList<ApplicationTaskOutput> previousOutputs,
        CancellationToken ct)
    {
        var inputs = new List<(string TaskName, string Output)>();
        foreach (var dependency in task.Dependencies)
        {
            var named = crewTasks.FirstOrDefault(t => t.Id == dependency);
            // The last successful attempt of the dependency, else its last attempt: a retry that recovered wins over the failure before it.
            var attempts = previousOutputs.Where(o => o.TaskId == dependency.Value.ToString()).ToList();
            var produced = attempts.LastOrDefault(o => o.Success) ?? attempts.LastOrDefault();
            if (named is not null && produced is not null)
                inputs.Add((DecisionPreflight.NameOf(named), produced.Content));
        }

        try
        {
            return await _runner.RunAsync(task, inputs, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A decision model that breaks fails its task, like an agent that fails: the dependants take the blocked path.
            return new TaskResult(false, string.Empty, null, [], TimeSpan.Zero, ex.Message);
        }
    }
}
