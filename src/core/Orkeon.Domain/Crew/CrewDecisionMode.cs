namespace Orkeon.Domain.Crew;

/// <summary>
/// Whether a crew needs the decision model, System One (<c>decision: { mode: ... }</c> at crew level,
/// <c>crewBuilder().decision({ mode })</c>, <see cref="CrewBuilder.DecisionMode"/>, #429). Every host applies
/// the same rules: the load rules of <see cref="Task.DecisionTaskRules.CheckMode"/>, and a <see cref="Required"/>
/// crew refused before its first task when the decision circuit is off.
/// </summary>
public enum CrewDecisionMode
{
    /// <summary>The crew declares no mode: it behaves as before the key existed (a decision task answers from its fallback with the circuit off).</summary>
    Unspecified = 0,

    /// <summary>The crew never asks System One anything: no decision task, no <c>system_one_decide</c>, no decision profile.</summary>
    None = 1,

    /// <summary>
    /// The crew runs with System One or without it, the launch picks which: every decision task declares a
    /// <c>without:</c> relay, which answers it with the circuit off.
    /// </summary>
    Optional = 2,

    /// <summary>The crew cannot run without System One: a run with the circuit off is refused before its first task.</summary>
    Required = 3,
}
