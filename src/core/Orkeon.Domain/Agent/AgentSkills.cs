namespace Orkeon.Domain.Agent;

/// <summary>
/// The skills an agent's prompt carries (<c>skills:</c>, #434): markdown playbooks read through the virtual
/// file system and injected into the agent's system prompt, each one and the whole block capped. Either a
/// fixed list (<see cref="Paths"/>), or <see cref="Auto"/>: for each task the decision model picks a few
/// skills of the host's library, and <see cref="Fallback"/> stands in when it cannot be asked.
/// </summary>
public sealed record AgentSkills
{
    /// <summary>The virtual paths of the skills the prompt always carries; empty when <see cref="Auto"/> picks them.</summary>
    public IReadOnlyList<string> Paths { get; init; } = [];

    /// <summary>Whether the decision model picks the skills of each task from the host's library.</summary>
    public bool Auto { get; init; }

    /// <summary>
    /// The virtual paths used instead of a pick when the decision circuit is off, the model fails or the
    /// library holds no skill; empty, the agent then runs with no skill.
    /// </summary>
    public IReadOnlyList<string> Fallback { get; init; } = [];
}
