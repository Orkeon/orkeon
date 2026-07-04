using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Represents a language code (e.g., "en", "fr", "es").
/// </summary>
public sealed record LanguageCode : ValueObjectRecord
{
    /// <summary>
    /// The default language code (English).
    /// </summary>
    public static readonly LanguageCode Default = new("en");

    /// <summary>
    /// Gets the language code value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the LanguageCode.
    /// </summary>
    /// <param name="value">The language code value.</param>
    /// <exception cref="ArgumentException">Thrown when the value is null or whitespace.</exception>
    private LanguageCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

#pragma warning disable CA1308 // lowercase is the stored/canonical form of the language code, not a comparison normalization
        var trimmed = value.Trim().ToLowerInvariant();
#pragma warning restore CA1308
        if (trimmed.Length > 10)
            throw new ArgumentException("Language code cannot exceed 10 characters.", nameof(value));

        Value = trimmed;
        Validate();
    }

    /// <summary>
    /// Creates a new LanguageCode from a string value.
    /// </summary>
    public static LanguageCode From(string value) => new(value);

    /// <summary>
    /// Implicit conversion to string.
    /// </summary>
    public static implicit operator string(LanguageCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.Value;
    }

    /// <summary>
    /// Returns the string representation of the language code.
    /// </summary>
    public override string ToString() => Value;
}
