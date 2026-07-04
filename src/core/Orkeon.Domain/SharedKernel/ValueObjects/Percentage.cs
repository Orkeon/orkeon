using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Percentage value object with validation.
/// </summary>
public sealed record Percentage : ValueObjectRecord
{
    /// <summary>Gets the percentage value (0–100).</summary>
    public double Value { get; }

    /// <summary>Initializes a new <see cref="Percentage"/> value.</summary>
    /// <param name="value">The percentage value (must be between 0 and 100).</param>
    private Percentage(double value)
    {
        if (value < 0)
            throw new ArgumentException("Percentage cannot be negative", nameof(value));
        if (value > 100)
            throw new ArgumentException("Percentage cannot exceed 100", nameof(value));

        Value = Math.Round(value, 2);
        Validate();
    }

    /// <summary>Gets the value as a decimal fraction (e.g., 50% → 0.5).</summary>
    public double AsDecimal => Value / 100.0;
    /// <summary>Gets the value as a ratio, equivalent to <see cref="AsDecimal"/>.</summary>
    public double AsRatio => AsDecimal;

    /// <summary>Creates a <see cref="Percentage"/> from a decimal fraction (e.g., 0.5 → 50%).</summary>
    /// <param name="decimalValue">The decimal fraction (0.0–1.0).</param>
    /// <returns>A <see cref="Percentage"/> instance.</returns>
    public static Percentage FromDecimal(double decimalValue) => From(decimalValue * 100);
    /// <summary>Creates a <see cref="Percentage"/> from a percentage value (0–100).</summary>
    /// <param name="value">The percentage value.</param>
    /// <returns>A <see cref="Percentage"/> instance.</returns>
    public static Percentage From(double value) => new(value);
    /// <summary>Implicitly converts a <see cref="Percentage"/> to a double.</summary>
    /// <param name="percentage">The percentage to convert.</param>
    public static implicit operator double(Percentage percentage)
    {
        ArgumentNullException.ThrowIfNull(percentage);
        return percentage.Value;
    }
    /// <summary>Friendly-named alternate for the implicit conversion to a double.</summary>
    /// <returns>The percentage value as a double.</returns>
    public double ToDouble() => Value;
    /// <inheritdoc />
    public override string ToString() => FormattableString.Invariant($"{Value:F1}%");
}
