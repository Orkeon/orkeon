using System.Collections.Immutable;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Told which profile a task of an agent on <c>llm.profile: auto</c> runs on (#434). The run protocol writes it as
/// <c>agent.route</c>; a host that registers none loses only that line, the choice is logged either way.
/// </summary>
public interface IAgentRouteObserver
{
    /// <summary>The profile of a task was chosen, once, at the task's start.</summary>
    /// <param name="notice">What was chosen, and why.</param>
    void ProfileRouted(AgentRouteNotice notice);
}

/// <summary>The profile a task of an <c>auto</c> agent runs on (#434): the fields of <c>agent.route</c>.</summary>
public sealed record AgentRouteNotice
{
    /// <summary>The task the agent works on.</summary>
    public required string TaskId { get; init; }

    /// <summary>The agent's role.</summary>
    public required string AgentRole { get; init; }

    /// <summary>The profile the task runs on: a rung of the ladder, or <see cref="LlmProfiles.Default"/>.</summary>
    public required string Profile { get; init; }

    /// <summary>One of <see cref="AgentRouteReasons"/>.</summary>
    public required string Reason { get; init; }

    /// <summary>The rung the decision model picked, when it answered with one; null otherwise.</summary>
    public string? Picked { get; init; }

    /// <summary>The probability the answer gave <see cref="Picked"/>; null when it picked no rung.</summary>
    public double? Probability { get; init; }

    /// <summary>The ladder offered, cheapest first.</summary>
    public required ImmutableList<string> Ladder { get; init; }
}

/// <summary>The <c>reason</c> of <c>agent.route</c>.</summary>
public static class AgentRouteReasons
{
    /// <summary>The decision model's pick, at a probability of 0.6 or more.</summary>
    public const string Model = "model";

    /// <summary>The pick's probability is under 0.6: the default profile stands.</summary>
    public const string LowConfidence = "low-confidence";

    /// <summary>The decision model gave no rung of the ladder, or its call failed: the default profile stands.</summary>
    public const string NoAnswer = "no-answer";

    /// <summary>The decision circuit is off: no call is made, and the default profile stands.</summary>
    public const string CircuitOff = "circuit-off";

    /// <summary>The host declares no ladder (a crew built by hand, never loaded): the default profile stands.</summary>
    public const string NoLadder = "no-ladder";

    /// <summary>
    /// The crew runs with <c>decision.mode: none</c> and asks System One nothing: no call is made, no warning is
    /// logged, and the default profile stands.
    /// </summary>
    public const string DecisionNone = "decision-none";
}
