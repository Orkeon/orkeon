using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Crew;

namespace Orkeon.Domain.Crew.ValueObjects;

/// <summary>
/// Represents the goal of a crew.
/// </summary>
public sealed record CrewGoal : ValueObjectRecord
{
    /// <summary>
    /// Gets the goal value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the CrewGoal.
    /// </summary>
    /// <param name="value">The goal value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is null or whitespace.</exception>
    private CrewGoal(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmed = value.Trim();
        if (trimmed.Length > CrewDefaults.CrewGoalMaxLength)
            throw new ArgumentException($"Crew goal cannot exceed {CrewDefaults.CrewGoalMaxLength} characters.", nameof(value));

        Value = trimmed;
        Validate();
    }

    /// <summary>
    /// Creates a new CrewGoal from a string value.
    /// </summary>
    public static CrewGoal From(string value) => new(value);

    /// <summary>
    /// Implicit conversion to string.
    /// </summary>
    public static implicit operator string(CrewGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return goal.Value;
    }

    /// <summary>
    /// Returns the string representation of the goal.
    /// </summary>
    public override string ToString() => Value;
}
