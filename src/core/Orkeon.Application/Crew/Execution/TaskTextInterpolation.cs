namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The run's variables in a task's text: each <c>{key}</c> replaced by its value, the key matched
/// case-insensitively; a placeholder no variable names is left as written. One function for every
/// reader of a task's description and expected output — the agent's prompt, the knowledge and memory
/// queries, and the crew's planner (GAP-31), which reads the tasks as their agents will.
/// </summary>
public static class TaskTextInterpolation
{
    /// <summary>Replaces the <c>{key}</c> placeholders of <paramref name="template"/> with the variables' values.</summary>
    /// <param name="template">A task's description or expected output.</param>
    /// <param name="variables">The run's variables; null or empty leaves the text as it is.</param>
    /// <returns>The text with the variables in it.</returns>
    public static string Interpolate(string template, IReadOnlyDictionary<string, string>? variables)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.Length == 0 || variables is null || variables.Count == 0)
            return template;

        var result = template;
        foreach (var variable in variables)
            result = result.Replace($"{{{variable.Key}}}", variable.Value, StringComparison.OrdinalIgnoreCase);

        return result;
    }
}
