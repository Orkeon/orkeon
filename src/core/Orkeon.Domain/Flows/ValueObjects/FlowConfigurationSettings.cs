using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Domain.Flows.ValueObjects;

/// <summary>
/// Strongly typed settings for flow configuration.
/// </summary>
[TypedDictionary(typeof(FlowSettingValue), CacheEmpty = true)]
public sealed partial class FlowConfigurationSettings : IEquatable<FlowConfigurationSettings>
{
    /// <summary>Gets a typed setting value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The setting key.</param>
    /// <returns>The typed value, or <see langword="default"/> if not found.</returns>
    public T? Get<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>Returns whether a setting exists for the given key.</summary>
    /// <param name="key">The setting key.</param>
    /// <returns><see langword="true"/> if the key exists; otherwise <see langword="false"/>.</returns>
    public bool ContainsKey(string key) => _items.ContainsKey(key);

    /// <summary>Gets all setting keys.</summary>
    public IEnumerable<string> Keys => _items.Keys;

    /// <summary>Gets the number of settings.</summary>
    public int Count => _items.Count;

    /// <summary>Returns a new instance with the specified setting set.</summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The value.</param>
    /// <returns>A new <see cref="FlowConfigurationSettings"/> with the value set.</returns>
    public FlowConfigurationSettings Set(string key, object value)
    {
        var newSettings = _items.SetItem(key, FlowSettingValue.From(value));
        return new FlowConfigurationSettings(newSettings);
    }

    /// <summary>Converts this instance to a <see cref="Dictionary{TKey,TValue}"/> of raw values.</summary>
    /// <returns>A dictionary of raw setting values.</returns>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in _items)
        {
            result[kvp.Key] = kvp.Value.RawValue;
        }
        return result;
    }

    /// <inheritdoc />
    public bool Equals(FlowConfigurationSettings? other)
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
    public override bool Equals(object? obj) => Equals(obj as FlowConfigurationSettings);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var kvp in _items.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value.RawValue);
        }
        return hash.ToHashCode();
    }

    /// <summary>Creates a <see cref="FlowConfigurationSettings"/> from a dictionary.</summary>
    /// <param name="dictionary">The source dictionary, or <see langword="null"/> for empty.</param>
    /// <returns>A new <see cref="FlowConfigurationSettings"/> instance.</returns>
    public static FlowConfigurationSettings FromDictionary(Dictionary<string, object>? dictionary)
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

    /// <summary>Builder for constructing <see cref="FlowConfigurationSettings"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets the degree of parallelism.</summary>
        /// <param name="degree">The maximum parallel degree.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("parallelism")]
        public partial Builder AddParallelism(int degree);

        /// <summary>Sets the retry policy.</summary>
        /// <param name="policy">The retry policy name.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("retry_policy")]
        public partial Builder AddRetryPolicy(string policy);

        /// <summary>Sets the error handling strategy.</summary>
        /// <param name="strategy">The error handling strategy name.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("error_handling")]
        public partial Builder AddErrorHandling(string strategy);

        /// <summary>Sets whether caching is enabled.</summary>
        /// <param name="enabled">Whether caching is enabled.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("cache_enabled")]
        public partial Builder AddCacheEnabled(bool enabled);

        /// <summary>Sets the state store type.</summary>
        /// <param name="storeType">The state store type name.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("state_store")]
        public partial Builder AddStateStore(string storeType);
    }
}

/// <summary>
/// Value wrapper for flow settings.
/// </summary>
public sealed class FlowSettingValue
{
    private readonly object _value;
    private readonly Type _type;

    private FlowSettingValue(object value, Type type)
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
                $"Cannot convert flow setting value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw underlying value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the value's type.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a <see cref="FlowSettingValue"/> from the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="FlowSettingValue"/>.</returns>
    public static FlowSettingValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new FlowSettingValue(value, value.GetType());
    }
}
