using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Application.Rag;

/// <summary>
/// Strongly typed metadata for knowledge context.
/// </summary>
[TypedDictionary(typeof(KnowledgeMetadataValue), EmitBuilderFrom = true)]
public sealed partial class KnowledgeContextMetadata
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
    /// Set.
    /// </summary>
    public KnowledgeContextMetadata Set(string key, object value)
    {
        var newMetadata = _items.SetItem(key, KnowledgeMetadataValue.From(value));
        return new KnowledgeContextMetadata(newMetadata);
    }

    /// <summary>
    /// To Dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in _items)
        {
            result[kvp.Key] = kvp.Value.RawValue;
        }
        return result;
    }

    /// <summary>Builder for constructing <see cref="KnowledgeContextMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Query.
        /// </summary>
        [DictionaryEntry("query")]
        public partial Builder AddQuery(string query);

        /// <summary>
        /// Add Source.
        /// </summary>
        [DictionaryEntry("source")]
        public partial Builder AddSource(string source);

        /// <summary>
        /// Add Timestamp.
        /// </summary>
        [DictionaryEntry("timestamp")]
        public partial Builder AddTimestamp(DateTime timestamp);

        /// <summary>
        /// Add Item Count.
        /// </summary>
        [DictionaryEntry("item_count")]
        public partial Builder AddItemCount(int count);

        /// <summary>
        /// Add Average Score.
        /// </summary>
        [DictionaryEntry("average_score")]
        public partial Builder AddAverageScore(float score);

        /// <summary>
        /// Add Context Type.
        /// </summary>
        [DictionaryEntry("context_type")]
        public partial Builder AddContextType(string contextType);

        /// <summary>
        /// Add Agent Id.
        /// </summary>
        [DictionaryEntry("agent_id")]
        public partial Builder AddAgentId(string agentId);
    }
}

/// <summary>
/// Value wrapper for knowledge metadata.
/// </summary>
public sealed class KnowledgeMetadataValue
{
    private readonly object _value;
    private readonly Type _type;

    private KnowledgeMetadataValue(object value, Type type)
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
                $"Cannot convert knowledge metadata value of type {_type.Name} to {typeof(T).Name}", ex);
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
    public static KnowledgeMetadataValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new KnowledgeMetadataValue(value, value.GetType());
    }
}
