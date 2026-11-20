namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>
/// The <c>without:</c> relay of a decision task (#429, #430): the agent of the crew that answers the task's
/// questions when an <c>optional</c> crew runs with the decision circuit off. It is not the task's agent: a
/// decision task still runs none.
/// </summary>
public sealed record DecisionRelay
{
    /// <summary>Initializes a relay.</summary>
    /// <param name="agent">The key of the crew agent that answers.</param>
    /// <param name="instructions">What is added to the questions, or null.</param>
    /// <exception cref="ArgumentException"><paramref name="agent"/> is empty.</exception>
    public DecisionRelay(string agent, string? instructions = null)
    {
        if (string.IsNullOrWhiteSpace(agent))
            throw new ArgumentException("A without: relay names the agent that answers: without.agent is required.", nameof(agent));
        Agent = agent.Trim();
        Instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions;
    }

    /// <summary>Gets the key of the crew agent that answers.</summary>
    public string Agent { get; }

    /// <summary>Gets the instructions added to the questions, or null.</summary>
    public string? Instructions { get; }
}
