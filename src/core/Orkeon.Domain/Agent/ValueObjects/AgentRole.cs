using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Domain.Agent.ValueObjects;

/// <summary>
/// Represents the role of an agent in the crew.
/// </summary>
public sealed record AgentRole : ValueObjectRecord
{
    /// <summary>
    /// Gets the role value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the AgentRole.
    /// </summary>
    /// <param name="value">The role value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is null or whitespace.</exception>
    private AgentRole(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();
        if (trimmed.Length > AgentDefaults.AgentRoleMaxLength)
            throw new ArgumentException($"Agent role cannot exceed {AgentDefaults.AgentRoleMaxLength} characters.", nameof(value));

        Value = trimmed;
        Validate();
    }

    /// <summary>
    /// Creates a new AgentRole from a string value.
    /// </summary>
    public static AgentRole From(string value) => new(value);

    /// <summary>
    /// Implicit conversion to string.
    /// </summary>
    public static implicit operator string(AgentRole role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return role.Value;
    }

    /// <summary>
    /// Returns the string representation of the role.
    /// </summary>
    public override string ToString() => Value;
}
