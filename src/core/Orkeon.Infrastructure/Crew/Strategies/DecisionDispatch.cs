using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using ApplicationTaskOutput = Orkeon.Application.Execution.TaskOutput;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// Runs one decision task through the decision runner, never through an agent; its <c>without:</c> relay agent
/// (#430) is the one crew agent it may ask. Taken once, the answer is replayed from the checkpoint on a resumed
/// run, in every process: the route a decision picked is part of the run (FWK-28).
/// </summary>
internal sealed class DecisionDispatch
{
    /// <summary>The role a decision task reports under: it has no agent.</summary>
    internal const string Role = "decision";

    private readonly DecisionTaskRunner _runner;
    private readonly Checkpointing.ICheckpointedDecisions? _checkpoints;

    internal DecisionDispatch(DecisionTaskRunner runner, IAgentExecutionService? executionService = null)
    {
        _runner = runner;
        _checkpoints = executionService as Checkpointing.ICheckpointedDecisions;
    }

    /// <summary>
    /// Runs <paramref name="task"/> over the outputs of the tasks it depends on, labelled by task name.
    /// </summary>
    /// <param name="task">The decision task.</param>
    /// <param name="crewTasks">The tasks of the crew, to name the dependencies.</param>
    /// <param name="agents">The agents of the crew, among which the relay agent.</param>
    /// <param name="previousOutputs">The outputs the run holds so far.</param>
    /// <param name="ct">The cancellation token.</param>
    internal async System.Threading.Tasks.Task<TaskResult> RunAsync(
        CrewTask task,
        IReadOnlyList<CrewTask> crewTasks,
        IReadOnlyList<Orkeon.Domain.Agent.Agent> agents,
        IReadOnlyList<ApplicationTaskOutput> previousOutputs,
        CancellationToken ct)
    {
        System.Threading.Tasks.Task<TaskResult> Decide() => DecideAsync(task, crewTasks, agents, previousOutputs, ct);
        return _checkpoints is null ? await Decide().ConfigureAwait(false) : await _checkpoints.RunDecisionAsync(task, Decide, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The crew agent the <c>without:</c> relay of <paramref name="task"/> names: by the id the crew factory
    /// resolved, else (a crew built in code) by a role equal to the key; null when there is none.
    /// </summary>
    internal static Orkeon.Domain.Agent.Agent? RelayAgentOf(CrewTask task, IReadOnlyList<Orkeon.Domain.Agent.Agent> agents) =>
        task.Decision?.Without is not { } relay
            ? null
            : relay.AgentId is { } id
                ? agents.FirstOrDefault(a => a.Id == id)
                : agents.FirstOrDefault(a => string.Equals(a.Role.Value, relay.Agent, StringComparison.Ordinal));

    private async System.Threading.Tasks.Task<TaskResult> DecideAsync(
        CrewTask task,
        IReadOnlyList<CrewTask> crewTasks,
        IReadOnlyList<Orkeon.Domain.Agent.Agent> agents,
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
            return await _runner.RunAsync(task, inputs, RelayAgentOf(task, agents), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A decision model that breaks fails its task, like an agent that fails: the dependants take the blocked path.
            return new TaskResult(false, string.Empty, null, [], TimeSpan.Zero, ex.Message);
        }
    }
}
