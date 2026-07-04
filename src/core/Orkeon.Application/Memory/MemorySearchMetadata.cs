using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Application.Memory;

/// <summary>
/// Strongly typed metadata for memory search results.
/// </summary>
[TypedDictionary(typeof(MemorySearchMetadataValue), CacheEmpty = true)]
public sealed partial class MemorySearchMetadata
{
    /// <summary>
    /// Get.
    /// </summary>
    public T? Get<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>
    /// Get String.
    /// </summary>
    public string? GetString(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return null;

        var obj = value.GetValue<object>();

        return obj switch
        {
            double d => d.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            null => null,
            _ => obj.ToString()
        };
    }

    /// <summary>
    /// Contains Key.
    /// </summary>
    public bool ContainsKey(string key) => _items.ContainsKey(key);

    /// <summary>
    /// Gets the keys.
    /// </summary>
    public IEnumerable<string> Keys => _items.Keys;

    /// <summary>
    /// Gets the number of entries.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// To Dictionary.
    /// </summary>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in _items)
        {
            result[kvp.Key] = kvp.Value.RawValue;
        }
        return result;
    }

    /// <summary>
    /// To String Dictionary.
    /// </summary>
    public Dictionary<string, string> ToStringDictionary()
    {
        var result = new Dictionary<string, string>();
        foreach (var kvp in _items)
        {
            var value = kvp.Value.RawValue;
            result[kvp.Key] = value switch
            {
                double d => d.ToString(CultureInfo.InvariantCulture),
                float f => f.ToString(CultureInfo.InvariantCulture),
                decimal dec => dec.ToString(CultureInfo.InvariantCulture),
                null => string.Empty,
                _ => value.ToString() ?? string.Empty
            };
        }
        return result;
    }

    /// <summary>
    /// From Dictionary.
    /// </summary>
    public static MemorySearchMetadata FromDictionary(IDictionary<string, object>? dictionary)
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

    /// <summary>Builder for constructing <see cref="MemorySearchMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Source.
        /// </summary>
        [DictionaryEntry("source")]
        public partial Builder AddSource(string source);

        /// <summary>
        /// Add Relevance.
        /// </summary>
        [DictionaryEntry("relevance")]
        public partial Builder AddRelevance(float relevance);

        /// <summary>
        /// Add Access Count.
        /// </summary>
        [DictionaryEntry("accessCount")]
        public partial Builder AddAccessCount(int count);

        /// <summary>
        /// Add Type.
        /// </summary>
        [DictionaryEntry("type")]
        public partial Builder AddType(string type);

        /// <summary>
        /// Add Agent Id.
        /// </summary>
        [DictionaryEntry("agent_id")]
        public partial Builder AddAgentId(string agentId);

        /// <summary>
        /// Add Context.
        /// </summary>
        [DictionaryEntry("context")]
        public partial Builder AddContext(string context);

        /// <summary>
        /// Add Created At.
        /// </summary>
        [DictionaryEntry("created_at")]
        public partial Builder AddCreatedAt(DateTime createdAt);

        /// <summary>
        /// Add Score.
        /// </summary>
        [DictionaryEntry("score")]
        public partial Builder AddScore(float score);
    }
}

/// <summary>
/// Value wrapper for memory search metadata.
/// </summary>
public sealed class MemorySearchMetadataValue
{
    private readonly object _value;
    private readonly Type _type;

    private MemorySearchMetadataValue(object value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
    }

    /// <summary>
    /// Get Value.
    /// </summary>
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
                $"Cannot convert memory search metadata value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>
    /// Raw Value.
    /// </summary>
    public object RawValue => _value;
    /// <summary>
    /// Value Type.
    /// </summary>
    public Type ValueType => _type;

    /// <summary>
    /// From.
    /// </summary>
    public static MemorySearchMetadataValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new MemorySearchMetadataValue(value, value.GetType());
    }
}
