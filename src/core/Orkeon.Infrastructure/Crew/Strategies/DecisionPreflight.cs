using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// The check a run makes once, at entry: with the decision circuit off, a decision task answers
/// from its fallback, so one that has neither a fallback nor an otherwise sibling would have
/// nothing to answer with.
/// </summary>
internal static class DecisionPreflight
{
    /// <summary>The name a decision task goes by in the crew's messages and in the runner's inputs.</summary>
    internal static string NameOf(CrewTask task) => task.Key ?? task.Description.Value;

    /// <summary>The decision task called <paramref name="name"/>; a plain task of that name is never taken for it.</summary>
    internal static CrewTask? FindDecisionTask(IReadOnlyList<CrewTask> tasks, string name) =>
        tasks.FirstOrDefault(t => t.IsDecision && string.Equals(NameOf(t), name, StringComparison.Ordinal));

    /// <summary>Lists the faults that stop the run, one message each; empty when it may start.</summary>
    internal static IReadOnlyList<string> Check(IReadOnlyList<CrewTask> tasks, DecisionTaskRunner? runner)
    {
        if (!tasks.Any(t => t.IsDecision || t.Condition is not null))
            return [];

        var faults = MissingDependencies(tasks);
        if (runner?.IsEnabled == true)
            return faults;

        return
        [
            .. faults,
            .. DecisionTaskRules.CheckFallbacks(
                [.. tasks.Select(t => new DecisionTaskDeclaration(NameOf(t), t.Decision, t.Condition, t.AssignedAgent is not null))])
        ];
    }

    /// <summary>A conditioned task must depend on the decision task its condition names, else it could run before the answer.</summary>
    private static List<string> MissingDependencies(IReadOnlyList<CrewTask> tasks)
    {
        var faults = new List<string>();
        foreach (var task in tasks.Where(t => t.Condition is not null))
        {
            var decision = FindDecisionTask(tasks, task.Condition!.DecisionTask);
            if (decision is null)
                faults.Add($"task '{NameOf(task)}' is conditioned on '{task.Condition!.DecisionTask}', which names no decision task of the crew");
            else if (!task.Dependencies.Contains(decision.Id))
                faults.Add($"task '{NameOf(task)}' is conditioned on decision task '{NameOf(decision)}' but does not depend on it");
        }

        return faults;
    }

    /// <summary>The run ends before its first task: the crew failed with <paramref name="error"/>, the terminal event sent.</summary>
    internal static async System.Threading.Tasks.Task<Orkeon.Domain.Crew.CrewOutput> FailAtEntryAsync(
        CrewHookDispatcher hooks, string crewId, DateTimeOffset startedAt, string error)
    {
        await hooks.CrewFailedAsync(
            CrewHookDispatcher.Snapshot(crewId, startedAt, [], Orkeon.Application.Crew.CrewHookStatus.Failed, error),
            null, CancellationToken.None).ConfigureAwait(false);
        return Orkeon.Domain.Crew.CrewOutput.CreateFailure(error, [], DateTimeOffset.UtcNow - startedAt);
    }
}
