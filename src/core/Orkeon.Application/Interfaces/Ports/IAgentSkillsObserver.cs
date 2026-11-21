using System.Collections.Immutable;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Told which skills an agent's prompt carries for a task (#434). The run protocol writes it as
/// <c>agent.skills</c>; a host that registers none loses only that line, the choice is logged either way.
/// </summary>
public interface IAgentSkillsObserver
{
    /// <summary>The skills of a task were chosen.</summary>
    /// <param name="notice">What was chosen, and why.</param>
    void SkillsChosen(AgentSkillsNotice notice);
}

/// <summary>Which skills a task's prompt carries (#434): the fields of <c>agent.skills</c>.</summary>
public sealed record AgentSkillsNotice
{
    /// <summary>The task the agent works on.</summary>
    public required string TaskId { get; init; }

    /// <summary>The agent's role.</summary>
    public required string AgentRole { get; init; }

    /// <summary>One of <see cref="AgentSkillsModes"/>.</summary>
    public required string Mode { get; init; }

    /// <summary>The virtual paths of the skills the prompt carries, in prompt order.</summary>
    public required ImmutableList<string> Skills { get; init; }

    /// <summary>The skills offered to the decision model: 0 unless <see cref="Mode"/> is <c>auto</c>.</summary>
    public required int Offered { get; init; }

    /// <summary>
    /// The skills cut short by a cap (said in the prompt too) or left out (the block's cap was spent, or the file
    /// could not be read; said here alone), by virtual path.
    /// </summary>
    public required ImmutableList<string> Cut { get; init; }

    /// <summary>Why the fallback stands (<see cref="AgentSkillsModes.Fallback"/>), one of <see cref="AgentSkillsReasons"/>; null otherwise.</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// What <see cref="Reason"/> means here, in a sentence: the circuit's off reason, the library and its fault, the
    /// decision model's error; null when no fallback stands.
    /// </summary>
    public string? Detail { get; init; }
}

/// <summary>The <c>reason</c> of <c>agent.skills</c> when its <c>mode</c> is <c>fallback</c>.</summary>
public static class AgentSkillsReasons
{
    /// <summary>The decision circuit is off (or no decision provider is registered): no call is made.</summary>
    public const string CircuitOff = "circuit-off";

    /// <summary>The library holds no skill, or could not be read: no call is made.</summary>
    public const string EmptyLibrary = "empty-library";

    /// <summary>The decision model's call failed, or its answer held no usable choice.</summary>
    public const string PickFailed = "pick-failed";
}

/// <summary>The <c>mode</c> of <c>agent.skills</c>.</summary>
public static class AgentSkillsModes
{
    /// <summary>The agent's fixed list.</summary>
    public const string List = "list";

    /// <summary>The decision model's pick from the library (possibly none).</summary>
    public const string Auto = "auto";

    /// <summary>
    /// An <c>auto</c> agent's fallback list, or no skill when it has none: the decision circuit is off, the
    /// model failed, or the library holds no skill.
    /// </summary>
    public const string Fallback = "fallback";
}
