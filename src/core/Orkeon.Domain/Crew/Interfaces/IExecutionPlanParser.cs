using Orkeon.Domain.Common;

namespace Orkeon.Domain.Crew.Interfaces;

/// <summary>
/// Reads the crew planner's reply (GAP-31): <c>{"plans": [{"task": 1, "plan": "…"}]}</c>, one
/// step-by-step plan per task, by the number the planning prompt gave the task. Kept out of the
/// Domain so the Domain stays free of any serialization technology.
/// </summary>
public interface IExecutionPlanParser
{
    /// <summary>Reads <paramref name="reply"/>.</summary>
    /// <param name="reply">What the planner answered, as it came back — fences and prose around the object included.</param>
    /// <param name="numberedTasks">The tasks in the order the planning prompt numbered them: task 1 first.</param>
    /// <returns>
    /// The plan, with a warning for each entry it ignored (a number given twice, a number the crew does
    /// not have, an entry without a number or a plan); or, when the reply holds no <c>plans</c> array,
    /// no plan and why.
    /// </returns>
    ExecutionPlanReading Read(string reply, IReadOnlyList<TaskId> numberedTasks);
}

/// <summary>What <see cref="IExecutionPlanParser.Read"/> made of a planner's reply.</summary>
public sealed record ExecutionPlanReading
{
    private ExecutionPlanReading(ExecutionPlan? plan, string? error, IReadOnlyList<string> warnings)
    {
        Plan = plan;
        Error = error;
        Warnings = warnings;
    }

    /// <summary>The plan the reply holds; null when it could not be read.</summary>
    public ExecutionPlan? Plan { get; }

    /// <summary>Why the reply could not be read; null when it was.</summary>
    public string? Error { get; }

    /// <summary>What the reader ignored in a reply it could read, one sentence each.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>A reply that was read.</summary>
    /// <param name="plan">The plan it holds.</param>
    /// <param name="warnings">What was ignored in it.</param>
    public static ExecutionPlanReading Of(ExecutionPlan plan, IReadOnlyList<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(warnings);
        return new(plan, null, warnings);
    }

    /// <summary>A reply that could not be read.</summary>
    /// <param name="error">Why, in words the planner is shown when it is asked again.</param>
    public static ExecutionPlanReading Unreadable(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new(null, error, []);
    }
}
