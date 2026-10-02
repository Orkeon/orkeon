namespace Orkeon.Domain.Constants.Crew;

/// <summary>
/// The bounds of crew planning (<c>planning: true</c>, GAP-31): how much of the crew the planner is
/// shown, and how much of its plan a task's prompt carries. The planning prompt grows with the crew
/// and the plan of a task is sent again on every turn of that task's agent loop, so both are cut —
/// with <see cref="TruncationMarker"/> — rather than left to the size of what the crew declares.
/// </summary>
public static class PlanningDefaults
{
    /// <summary>How much of a task's description the planner reads (the domain allows 32 768 characters).</summary>
    public const int MaxTaskDescriptionChars = 1_500;

    /// <summary>How much of a task's expected output the planner reads (the domain allows 4 096 characters).</summary>
    public const int MaxExpectedOutputChars = 500;

    /// <summary>How much of an agent's goal the planner reads (the domain allows 2 048 characters).</summary>
    public const int MaxAgentGoalChars = 300;

    /// <summary>How many of an agent's tool names the planner reads; the rest are counted, not named.</summary>
    public const int MaxToolNamesPerAgent = 15;

    /// <summary>How much of the run's variables, all together, the planner reads.</summary>
    public const int MaxVariablesChars = 2_000;

    /// <summary>How much of a task's plan the task's prompt carries.</summary>
    public const int MaxPlanChars = 2_000;

    /// <summary>How many numbered steps the planner is asked for, at most, per task.</summary>
    public const int MaxPlanSteps = 8;

    /// <summary>The planning call's temperature: a plan is wanted consistent, not creative.</summary>
    public const float Temperature = 0.3f;

    /// <summary>What ends a text cut at its bound.</summary>
    public const string TruncationMarker = " […]";
}
