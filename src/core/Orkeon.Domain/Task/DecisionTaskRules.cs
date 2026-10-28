using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Task;

/// <summary>Crew-level rules of decision tasks.</summary>
public static class DecisionTaskRules
{
    /// <summary>The refusal of a task that names an agent and declares a decision.</summary>
    public static string AgentAndDecisionMessage(string taskName) =>
        $"Task '{taskName}' declares both a decision and an agent: a decision task runs no agent, remove one of decision and agent.";

    /// <summary>The refusal of a decision or a conditioned task in a process that does not run them.</summary>
    public static string UnsupportedProcessMessage(string taskName, ProcessType process) =>
        $"Task '{taskName}' declares a decision or a condition, which the {process} process does not support.";

    /// <summary>Checks the declarations, one message per fault.</summary>
    public static IReadOnlyList<string> Check(ProcessType process, IReadOnlyList<DecisionTaskDeclaration> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var messages = new List<string>();
        var supported = process == ProcessType.Sequential || process == ProcessType.Parallel || process == ProcessType.Graph;
        var otherwiseSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var task in tasks)
        {
            if (task.Decision is not null && task.HasAgent)
                messages.Add(AgentAndDecisionMessage(task.Name));

            if (!supported && (task.Decision is not null || task.Condition is not null))
                messages.Add(UnsupportedProcessMessage(task.Name, process));

            if (task.Condition is { } condition)
                CheckCondition(task.Name, condition, tasks, otherwiseSeen, messages);
        }

        return messages;
    }

    /// <summary>Lists the decision tasks that have no fallback.</summary>
    public static IReadOnlyList<string> CheckFallbacks(IReadOnlyList<DecisionTaskDeclaration> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        return tasks
            .Where(t => t.Decision is { HasExplicitFallback: false }
                && !tasks.Any(o => o.Condition is { IsOtherwise: true } c && c.DecisionTask == t.Name))
            .Select(t => $"Decision task '{t.Name}' has neither a fallback nor an otherwise task.")
            .ToList();
    }

    private static void CheckCondition(
        string name,
        TaskCondition condition,
        IReadOnlyList<DecisionTaskDeclaration> tasks,
        HashSet<string> otherwiseSeen,
        List<string> messages)
    {
        var target = tasks.FirstOrDefault(t => t.Name == condition.DecisionTask);
        if (target?.Decision is not { } decision)
        {
            messages.Add($"Task '{name}' has a condition on '{condition.DecisionTask}', which is not a decision task.");
            return;
        }

        if (condition.IsOtherwise)
        {
            if (!otherwiseSeen.Add(condition.DecisionTask))
                messages.Add($"Task '{name}' duplicates the otherwise task of '{condition.DecisionTask}'.");
            else if (!tasks.Any(t => t.Condition is { IsOtherwise: false } c && c.DecisionTask == condition.DecisionTask))
                messages.Add($"Task '{name}' is an otherwise task of '{condition.DecisionTask}' with no sibling condition.");
            return;
        }

        foreach (var clause in condition.Clauses)
        {
            var question = decision.Question(clause.Question);
            if (question is null)
            {
                messages.Add($"Task '{name}' names the unknown question '{clause.Question}' of '{condition.DecisionTask}'.");
                continue;
            }

            var named = clause.Options.Concat(clause.Option is null ? [] : [clause.Option]);
            if (question is ChoiceQuestionSpec choice && named.FirstOrDefault(o => !choice.Options.ContainsKey(o)) is { } unknown)
                messages.Add($"Task '{name}' names the unknown option '{unknown}' of question '{clause.Question}'.");
        }
    }
}
