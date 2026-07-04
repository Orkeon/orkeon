using Orkeon.Domain.Common;

namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Configuration key value object for type-safe configuration access.
/// </summary>
public record ConfigurationKey : ValueObjectRecord
{
    /// <summary>Gets the configuration key string.</summary>
    public string Value { get; }
    /// <summary>Gets the expected value type for this configuration key.</summary>
    public Type ValueType { get; }

    /// <summary>Initializes a new <see cref="ConfigurationKey"/>.</summary>
    /// <param name="value">The configuration key string (non-empty).</param>
    /// <param name="valueType">The expected value type.</param>
    private protected ConfigurationKey(string value, Type valueType)
    {
        Value = string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Configuration key cannot be empty", nameof(value)) : value;
        ArgumentNullException.ThrowIfNull(valueType);
        ValueType = valueType;
    }

    /// <summary>Creates a new <see cref="ConfigurationKey"/>.</summary>
    /// <param name="value">The configuration key string (non-empty).</param>
    /// <param name="valueType">The expected value type.</param>
    /// <returns>A new <see cref="ConfigurationKey"/>.</returns>
    public static ConfigurationKey From(string value, Type valueType) => new(value, valueType);

    /// <summary>Creates a typed <see cref="ConfigurationKey{T}"/> for the given key string.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The configuration key string.</param>
    /// <returns>A new <see cref="ConfigurationKey{T}"/>.</returns>
    public static ConfigurationKey<T> Create<T>(string key) => ConfigurationKey<T>.From(key);
    /// <inheritdoc />
    public override string ToString() => $"{Value} ({ValueType.Name})";
}

/// <summary>
/// Generic configuration key for compile-time type safety.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "Idiomatic static factory on a generic value-object type.")]
public sealed record ConfigurationKey<T> : ConfigurationKey
{
    /// <summary>Gets the configuration key string.</summary>
    public new string Value { get; }

    private ConfigurationKey(string value) : base(value, typeof(T))
    {
        Value = value;
    }

    /// <summary>Creates a new <see cref="ConfigurationKey{T}"/>.</summary>
    /// <param name="value">The configuration key string.</param>
    /// <returns>A new <see cref="ConfigurationKey{T}"/>.</returns>
    public static ConfigurationKey<T> From(string value) => new(value);

    /// <summary>Implicitly converts a <see cref="ConfigurationKey{T}"/> to a string.</summary>
    /// <param name="key">The configuration key to convert.</param>
    public static implicit operator string(ConfigurationKey<T> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.Value;
    }
}
