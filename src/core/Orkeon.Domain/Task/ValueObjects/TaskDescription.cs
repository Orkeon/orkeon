using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Task;

namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>
/// Represents the description of a task.
/// </summary>
public sealed record TaskDescription : ValueObjectRecord
{
    /// <summary>
    /// Gets the description value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the TaskDescription.
    /// </summary>
    /// <param name="value">The description value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is null or whitespace.</exception>
    private TaskDescription(string value)
    {
        EnsureNotNullOrWhiteSpace(value, nameof(value));

        var trimmed = value.Trim();
        if (trimmed.Length > TaskDefaults.TaskDescriptionMaxLength)
            throw new ArgumentException($"Task description cannot exceed {TaskDefaults.TaskDescriptionMaxLength} characters.", nameof(value));

        Value = trimmed;
        Validate();
    }

    /// <summary>
    /// Creates a new TaskDescription from a string value.
    /// </summary>
    public static TaskDescription From(string value)
    {
        // Reject null/empty/whitespace uniformly with one ArgumentException (same
        // contract as the constructor's guard) rather than a separate ThrowIfNull that
        // would surface ArgumentNullException for the null case. Also satisfies CA1062.
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{nameof(value)} cannot be null or whitespace.", nameof(value));
        return new(value);
    }

    /// <summary>
    /// Implicit conversion to string.
    /// </summary>
    public static implicit operator string(TaskDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return description.Value;
    }

    /// <summary>
    /// Returns the string representation of the description.
    /// </summary>
    public override string ToString() => Value;
}
