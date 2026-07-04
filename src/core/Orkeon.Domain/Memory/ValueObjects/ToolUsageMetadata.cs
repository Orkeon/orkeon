using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Domain.Memory.ValueObjects;

/// <summary>
/// Strongly typed metadata for tool usage.
/// </summary>
[TypedDictionary(typeof(ToolUsageMetadataValue), CacheEmpty = true)]
public sealed partial class ToolUsageMetadata : IEquatable<ToolUsageMetadata>
{
    /// <summary>Gets a typed metadata value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The metadata key.</param>
    /// <returns>The typed value, or <see langword="default"/> if not found.</returns>
    public T? Get<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>Gets a required typed metadata value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The metadata key.</param>
    /// <returns>The typed value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the key does not exist.</exception>
    public T GetRequired<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            throw new KeyNotFoundException($"Required metadata key '{key}' not found");

        return value.GetValue<T>();
    }

    /// <summary>Returns whether a metadata entry exists for the given key.</summary>
    /// <param name="key">The metadata key.</param>
    /// <returns><see langword="true"/> if the key exists; otherwise <see langword="false"/>.</returns>
    public bool ContainsKey(string key) => _items.ContainsKey(key);

    /// <summary>Gets all metadata keys.</summary>
    public IEnumerable<string> Keys => _items.Keys;

    /// <summary>Converts this instance to a <see cref="Dictionary{TKey,TValue}"/> of raw values.</summary>
    /// <returns>A dictionary of raw metadata values.</returns>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in _items)
        {
            result[kvp.Key] = kvp.Value.RawValue;
        }
        return result;
    }

    /// <summary>Creates a <see cref="ToolUsageMetadata"/> from a dictionary.</summary>
    /// <param name="dictionary">The source dictionary, or <see langword="null"/> for empty.</param>
    /// <returns>A new <see cref="ToolUsageMetadata"/> instance.</returns>
    public static ToolUsageMetadata FromDictionary(IDictionary<string, object>? dictionary)
    {
        if (dictionary == null || dictionary.Count == 0)
            return Empty;

        var builder = CreateBuilder();
        foreach (var kvp in dictionary)
        {
            builder.Add(kvp.Key, kvp.Value);
        }
        return builder.Build();
    }

    /// <inheritdoc />
    public bool Equals(ToolUsageMetadata? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_items.Count != other._items.Count) return false;

        foreach (var kvp in _items)
        {
            if (!other._items.TryGetValue(kvp.Key, out var otherValue))
                return false;
            if (!Equals(kvp.Value.RawValue, otherValue.RawValue))
                return false;
        }
        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ToolUsageMetadata);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_items.Count);
        foreach (var kvp in _items.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value.RawValue);
        }
        return hash.ToHashCode();
    }

    /// <summary>Builder for constructing <see cref="ToolUsageMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets the tool input.</summary>
        /// <param name="input">The input text.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("input")]
        public partial Builder AddInput(string input);

        /// <summary>Sets the tool output.</summary>
        /// <param name="output">The output text.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("output")]
        public partial Builder AddOutput(string output);

        /// <summary>Sets the retry count.</summary>
        /// <param name="retryCount">The number of retries.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("retry_count")]
        public partial Builder AddRetryCount(int retryCount);

        /// <summary>Sets whether the result was a cache hit.</summary>
        /// <param name="cacheHit">Whether the result came from cache.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("cache_hit")]
        public partial Builder AddCacheHit(bool cacheHit);

        /// <summary>Sets the resources consumed during execution.</summary>
        /// <param name="resources">A map of resource names to their usage values.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("resources_used")]
        public partial Builder AddResourcesUsed(Dictionary<string, double> resources);

        /// <summary>Sets the execution context description.</summary>
        /// <param name="context">The execution context text.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("execution_context")]
        public partial Builder AddExecutionContext(string context);

        /// <summary>Sets the validation errors encountered.</summary>
        /// <param name="errors">The array of validation error messages.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("validation_errors")]
        public partial Builder AddValidationErrors(string[] errors);

        /// <summary>Sets the performance metrics.</summary>
        /// <param name="metrics">A map of metric names to their values.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("performance_metrics")]
        public partial Builder AddPerformanceMetrics(Dictionary<string, double> metrics);
    }
}

/// <summary>
/// Value wrapper for tool usage metadata.
/// </summary>
public sealed class ToolUsageMetadataValue
{
    private readonly object _value;
    private readonly Type _type;

    private ToolUsageMetadataValue(object value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
    }

    /// <summary>Gets the typed value.</summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <returns>The typed value.</returns>
    public T GetValue<T>()
    {
        if (_value is T typedValue)
            return typedValue;

        try
        {
            return (T)Convert.ChangeType(_value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new InvalidCastException(
                $"Cannot convert tool usage metadata value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw underlying value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the value's type.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a <see cref="ToolUsageMetadataValue"/> from the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="ToolUsageMetadataValue"/>.</returns>
    public static ToolUsageMetadataValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new ToolUsageMetadataValue(value, value.GetType());
    }
}
