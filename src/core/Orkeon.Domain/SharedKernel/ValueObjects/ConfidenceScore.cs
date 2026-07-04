using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Value object representing a confidence score between 0.0 and 1.0.
/// Encapsulates business rules for confidence degradation after plan refinement.
/// </summary>
public sealed record ConfidenceScore
{
    /// <summary>
    /// The default degradation factor applied when a plan is refined after failure.
    /// </summary>
    public const double DefaultDegradationFactor = 0.9;

    /// <summary>
    /// Gets the confidence value (0.0 to 1.0).
    /// </summary>
    public double Value { get; }

    /// <summary>
    /// Creates a new <see cref="ConfidenceScore"/> with the given value.
    /// </summary>
    /// <param name="value">The confidence value, must be between 0.0 and 1.0 inclusive.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="value"/> is outside [0.0, 1.0].</exception>
    private ConfidenceScore(double value)
    {
        if (value < 0.0 || value > 1.0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Confidence score must be between 0.0 and 1.0.");

        Value = value;
    }

    /// <summary>
    /// Creates a new <see cref="ConfidenceScore"/> with the given value.
    /// </summary>
    /// <param name="value">The confidence value, must be between 0.0 and 1.0 inclusive.</param>
    /// <returns>A new <see cref="ConfidenceScore"/>.</returns>
    public static ConfidenceScore From(double value) => new(value);

    /// <summary>
    /// Returns a new <see cref="ConfidenceScore"/> degraded by the given factor.
    /// This models the business rule that confidence decreases after a plan refinement cycle.
    /// </summary>
    /// <param name="factor">The degradation factor (default 0.9). Must be between 0.0 and 1.0.</param>
    /// <returns>A new <see cref="ConfidenceScore"/> with the degraded value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="factor"/> is outside [0.0, 1.0].</exception>
    public ConfidenceScore Degrade(double factor = DefaultDegradationFactor)
    {
        if (factor < 0.0 || factor > 1.0)
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "Degradation factor must be between 0.0 and 1.0.");

        return From(Value * factor);
    }

    /// <summary>
    /// Implicit conversion from <see cref="ConfidenceScore"/> to <see langword="double"/>.
    /// </summary>
    public static implicit operator double(ConfidenceScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        return score.Value;
    }

    /// <summary>Friendly-named alternate for the implicit conversion to <see langword="double"/>.</summary>
    /// <returns>The score value as a <see langword="double"/>.</returns>
    public double ToDouble() => Value;

    /// <summary>
    /// Explicit conversion from <see langword="double"/> to <see cref="ConfidenceScore"/>.
    /// </summary>
    public static explicit operator ConfidenceScore(double value) => From(value);

    /// <summary>Friendly-named alternate for the explicit conversion from <see langword="double"/>.</summary>
    /// <param name="value">The confidence value.</param>
    /// <returns>A new <see cref="ConfidenceScore"/>.</returns>
    public static ConfidenceScore FromDouble(double value) => From(value);

    /// <inheritdoc />
    public override string ToString() => Inv.ToString(Value, "F2");
}
