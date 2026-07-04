using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Task;

namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>
/// Represents the expected output of a task.
/// </summary>
public sealed record ExpectedOutput : ValueObjectRecord
{
    /// <summary>
    /// Gets the expected output value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the ExpectedOutput.
    /// </summary>
    /// <param name="value">The expected output value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is null or whitespace.</exception>
    private ExpectedOutput(string value)
    {
        EnsureNotNullOrWhiteSpace(value, nameof(value));

        var trimmed = value.Trim();
        if (trimmed.Length > TaskDefaults.ExpectedOutputMaxLength)
            throw new ArgumentException($"Expected output cannot exceed {TaskDefaults.ExpectedOutputMaxLength} characters.", nameof(value));

        Value = trimmed;
        Validate();
    }

    /// <summary>
    /// Creates a new ExpectedOutput from a string value.
    /// </summary>
    public static ExpectedOutput From(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value);
    }

    /// <summary>
    /// Implicit conversion to string.
    /// </summary>
    public static implicit operator string(ExpectedOutput expectedOutput)
    {
        ArgumentNullException.ThrowIfNull(expectedOutput);
        return expectedOutput.Value;
    }

    /// <summary>
    /// Returns the string representation of the expected output.
    /// </summary>
    public override string ToString() => Value;
}
