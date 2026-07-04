using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Domain.Common;

/// <summary>
/// Base record for value objects in Domain-Driven Design.
/// Value objects are immutable and represent descriptive aspects of the domain.
/// This base record provides common functionality for record-based value objects.
/// </summary>
public abstract record ValueObjectRecord
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    /// <summary>Serializes this value object to a JSON string.</summary>
    /// <returns>A JSON representation of this value object.</returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, this.GetType(), s_jsonOptions);
    }

    /// <summary>
    /// Validates the value object state.
    /// Override this method to implement custom validation logic.
    /// </summary>
    protected virtual void Validate()
    {
        // Default implementation does nothing
        // Derived classes can override to add validation
    }

    /// <summary>
    /// Creates a defensive copy of a collection.
    /// </summary>
    protected static IReadOnlyDictionary<TKey, TValue> CreateDefensiveCopy<TKey, TValue>(
        IDictionary<TKey, TValue>? source) where TKey : notnull
    {
        return source != null
            ? new Dictionary<TKey, TValue>(source)
            : [];
    }

    /// <summary>
    /// Creates a defensive copy of a list.
    /// </summary>
    protected static IReadOnlyList<T> CreateDefensiveCopy<T>(IEnumerable<T>? source)
    {
        return source?.ToList() ?? [];
    }

    /// <summary>
    /// Ensures a string is not null or whitespace.
    /// </summary>
    protected static string EnsureNotNullOrWhiteSpace(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameterName} cannot be null or whitespace.", parameterName);

        return value;
    }

    /// <summary>
    /// Ensures a value is not null.
    /// </summary>
    protected static T EnsureNotNull<T>(T? value, string parameterName) where T : class
    {
        if (value is null)
            throw new ArgumentNullException(parameterName);
        return value;
    }

    /// <summary>
    /// Ensures a value is within a specified range.
    /// </summary>
    protected static T EnsureInRange<T>(T value, T min, T max, string parameterName)
        where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
            throw new ArgumentOutOfRangeException(parameterName,
                $"{parameterName} must be between {min} and {max}.");

        return value;
    }

    /// <summary>
    /// Ensures a value is positive.
    /// </summary>
    protected static T EnsurePositive<T>(T value, string parameterName)
        where T : IComparable<T>, IComparable
    {
        if (value.CompareTo(default(T)) <= 0)
            throw new ArgumentException($"{parameterName} must be positive.", parameterName);

        return value;
    }

    /// <summary>
    /// Ensures a value is non-negative.
    /// </summary>
    protected static T EnsureNonNegative<T>(T value, string parameterName)
        where T : IComparable<T>, IComparable
    {
        if (value.CompareTo(default(T)) < 0)
            throw new ArgumentException($"{parameterName} cannot be negative.", parameterName);

        return value;
    }
}
