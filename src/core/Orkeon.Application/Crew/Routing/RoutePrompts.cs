namespace Orkeon.Application.Crew.Routing;

/// <summary>The texts of the question the decision model answers for an agent on <c>llm.profile: auto</c> (#434).</summary>
internal static class RoutePrompts
{
    /// <summary>The instructions of the choice question.</summary>
    internal const string QuestionInstructions =
        "Which model should carry out the task described in the state? The options run from the cheapest to the strongest; pick the cheapest that will do the task well.";

    /// <summary>The cheapest rung.</summary>
    internal const string Lowest = "The cheapest, fastest model is enough: a lookup, a summary, a mechanical or fully specified task.";

    /// <summary>A rung between the cheapest and the strongest.</summary>
    internal const string Middle = "A mid-range model fits: more than a mechanical task, less than a design or a change that spans several parts.";

    /// <summary>The strongest rung.</summary>
    internal const string Highest = "The strongest model is needed: a design, a change that spans several parts, subtle reasoning, a review that must not miss a defect.";

    /// <summary>The state the decision model judges: the agent and its task.</summary>
    internal static string State(string role, string request) =>
        $"An agent with the role '{role}' is about to carry out this task:\n{request}";

    /// <summary>What stands for the middle of a task cut to fit.</summary>
    internal const string Elision = "\n[...]\n";
}
