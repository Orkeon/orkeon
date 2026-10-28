using Orkeon.Domain.Task;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// Decides whether a task whose dependencies did not fail runs: its condition on the answer of its
/// decision task, and the join rule — a task whose every dependency was condition-skipped is skipped.
/// </summary>
internal static class ConditionGate
{
    /// <summary>
    /// Whether <paramref name="task"/> runs, and why not when it does not.
    /// </summary>
    /// <param name="task">The task about to be dispatched.</param>
    /// <param name="outcome">What the run recorded so far.</param>
    /// <param name="crewTasks">The tasks of the crew, to find the decision task and the sibling conditions.</param>
    internal static (bool Run, string? Reason) ShouldRun(CrewTask task, CrewRunOutcome outcome, IReadOnlyList<CrewTask> crewTasks)
    {
        if (task.Condition is { } condition)
            return Evaluate(task, condition, outcome, crewTasks);

        return task.Dependencies.Count > 0 && task.Dependencies.All(outcome.IsConditionSkipped)
            ? (false, "every dependency was condition skipped")
            : (true, null);
    }

    private static (bool Run, string? Reason) Evaluate(
        CrewTask task, Domain.Task.ValueObjects.TaskCondition condition, CrewRunOutcome outcome, IReadOnlyList<CrewTask> crewTasks)
    {
        var decisionTask = crewTasks.FirstOrDefault(t => DecisionPreflight.NameOf(t) == condition.DecisionTask);
        if (decisionTask?.Decision is not { } decision)
            return (true, null);

        if (outcome.IsConditionSkipped(decisionTask.Id))
            return (false, $"decision {condition.DecisionTask} did not run");

        if (outcome.DecisionOf(decisionTask.Id) is not { } answer)
            return (true, null);

        var siblings = crewTasks
            .Select(t => t.Condition)
            .OfType<Domain.Task.ValueObjects.TaskCondition>()
            .Where(c => c.DecisionTask == condition.DecisionTask && !c.IsOtherwise)
            .ToList();
        var holds = condition.IsOtherwise
            ? condition.OtherwiseHolds(answer, decision, siblings)
            : condition.Holds(answer, decision);
        return holds ? (true, null) : (false, $"condition of task {task.Id} did not hold on decision task {condition.DecisionTask}: {answer.ToCompactJson()}");
    }
}
