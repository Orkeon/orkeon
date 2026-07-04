using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Domain.Agent.ValueObjects;

/// <summary>
/// Represents the goal of an agent.
/// </summary>
public sealed record AgentGoal : ValueObjectRecord
{
    /// <summary>
    /// Gets the goal value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the AgentGoal.
    /// </summary>
    /// <param name="value">The goal value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is null or whitespace.</exception>
    private AgentGoal(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > AgentDefaults.AgentGoalMaxLength)
            throw new ArgumentException($"Agent goal cannot exceed {AgentDefaults.AgentGoalMaxLength} characters.", nameof(value));

        Value = value.Trim();
    }

    /// <summary>
    /// Creates a new AgentGoal from a string value.
    /// </summary>
    public static AgentGoal From(string value) => new(value);

    /// <summary>
    /// Implicit conversion to string.
    /// </summary>
    public static implicit operator string(AgentGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return goal.Value;
    }

    /// <summary>
    /// Returns the string representation of the goal.
    /// </summary>
    public override string ToString() => Value;
}
