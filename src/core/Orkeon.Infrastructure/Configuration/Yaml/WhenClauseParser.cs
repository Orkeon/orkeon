using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>Parses the <c>when:</c> map of a task into a <see cref="TaskCondition"/>; every rule lives in the domain's Create.</summary>
internal static class WhenClauseParser
{
    /// <summary>The name of the decision task a <c>when:</c> map reads, from its first key.</summary>
    internal static string DecisionTaskOf(Dictionary<string, object> when) =>
        when.Keys.First().Split('.')[0];

    /// <summary>Builds the condition of <paramref name="when"/> against the decision of its task.</summary>
    internal static TaskCondition Parse(Dictionary<string, object> when, TaskDecision decision)
    {
        var decisionTask = DecisionTaskOf(when);
        var clauses = new List<ConditionClause>();
        foreach (var (key, value) in when)
        {
            var parts = key.Split('.');
            if (parts[0] != decisionTask)
                throw new ArgumentException($"A condition reads one decision task, not '{decisionTask}' and '{parts[0]}'.", nameof(when));
            if (parts.Length == 1)
            {
                if (value as string != "otherwise")
                    throw new ArgumentException($"'{key}' takes the value otherwise.", nameof(when));
                if (when.Count > 1)
                    throw new ArgumentException($"'otherwise' stands alone and cannot be mixed with other clauses.", nameof(when));
                return TaskCondition.Otherwise(decisionTask);
            }

            if (parts.Length > 3)
                throw new ArgumentException($"'{key}' has more than three segments (task.question.option).", nameof(when));
            clauses.Add(value switch
            {
                string text => ParseThreshold(decision, parts[1], parts.Length > 2 ? parts[2] : null, text),
                IEnumerable<object> when parts.Length > 2 =>
                    throw new ArgumentException($"'{key}' names an option and takes a threshold string, not a list.", nameof(when)),
                IEnumerable<object> list when list.All(o => o is string) =>
                    ConditionClause.OneOf(decision, parts[1], list.Select(o => (string)o).ToList()),
                _ => throw new ArgumentException($"'{key}' takes a list of strings or a threshold string.", nameof(when)),
            });
        }

        return TaskCondition.Create(decisionTask, clauses);
    }

    private static ConditionClause ParseThreshold(TaskDecision decision, string question, string? option, string text) =>
        ThresholdExpression.TryParse(text, out var comparison, out var number)
            ? ConditionClause.Threshold(decision, question, option, comparison, number)
            : throw new ArgumentException($"'{text}' is not a threshold such as '>= 0.7'.", nameof(text));
}
