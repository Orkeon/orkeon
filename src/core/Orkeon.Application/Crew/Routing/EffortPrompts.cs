namespace Orkeon.Application.Crew.Routing;

/// <summary>The texts of the question the decision model answers for an agent or a task on <c>thinking: { effort: auto }</c> (#439).</summary>
internal static class EffortPrompts
{
    /// <summary>The instructions of the choice question.</summary>
    internal const string QuestionInstructions =
        "How much should the model think before it answers, for the task described in the state? The options run from the least thinking to the most; pick the least that will do the task well.";

    /// <summary>The lowest level offered.</summary>
    internal const string Lowest = "Little thinking is enough: a lookup, a summary, a mechanical or fully specified task.";

    /// <summary>A level between the lowest and the highest offered.</summary>
    internal const string Middle = "Some thinking fits: more than a mechanical task, less than a design or a change that spans several parts.";

    /// <summary>The highest level offered.</summary>
    internal const string Highest = "Deep thinking is needed: a design, a change that spans several parts, subtle reasoning, a review that must not miss a defect.";
}
