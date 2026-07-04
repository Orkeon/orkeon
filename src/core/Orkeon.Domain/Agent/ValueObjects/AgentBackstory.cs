using Orkeon.Domain.Common;

namespace Orkeon.Domain.Agent.ValueObjects;

/// <summary>
/// Represents the backstory of an agent.
/// </summary>
public sealed record AgentBackstory : ValueObjectRecord
{
    /// <summary>
    /// Gets the backstory value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the AgentBackstory.
    /// </summary>
    /// <param name="value">The backstory value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is null or whitespace.</exception>
    private AgentBackstory(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Value = value.Trim();
    }

    /// <summary>
    /// Creates a new AgentBackstory from a string value.
    /// </summary>
    public static AgentBackstory From(string value) => new(value);

    /// <summary>
    /// Implicit conversion to string.
    /// </summary>
    public static implicit operator string(AgentBackstory backstory)
    {
        ArgumentNullException.ThrowIfNull(backstory);
        return backstory.Value;
    }

    /// <summary>
    /// Returns the string representation of the backstory.
    /// </summary>
    public override string ToString() => Value;
}
