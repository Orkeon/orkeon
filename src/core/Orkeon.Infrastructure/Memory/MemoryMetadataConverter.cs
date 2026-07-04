using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Domain.Memory.ValueObjects;
using Orkeon.Generators;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// Strongly typed memory metadata converter for Infrastructure layer.
/// </summary>
public static class MemoryMetadataConverter
{
    /// <summary>
    /// Converts MemoryMetadata to a dictionary for serialization.
    /// </summary>
    public static SerializableMemoryMetadata ConvertToSerializable(MemoryMetadata? metadata)
    {
        if (metadata == null)
            return SerializableMemoryMetadata.Empty;

        var builder = SerializableMemoryMetadata.CreateBuilder()
            .AddCreatedAt(metadata.CreatedAt)
            .AddSource(metadata.Source)
            .AddRelevance((float)metadata.Relevance)
            .AddAccessCount(metadata.AccessCount);

        if (metadata.LastAccessedAt.HasValue)
            builder.AddLastAccessedAt(metadata.LastAccessedAt.Value);

        if (metadata.Tags != null && metadata.Tags.Count > 0)
            builder.AddTags(metadata.Tags.ToArray());

        if (metadata.CreatedBy != null)
            builder.AddCreatedBy(metadata.CreatedBy.ToString());

        if (metadata.CustomProperties != null)
        {
            foreach (var kvp in metadata.CustomProperties)
                builder.AddCustomProperty(kvp.Key, kvp.Value);
        }

        return builder.Build();
    }

    /// <summary>
    /// Converts deserialized metadata back to domain format.
    /// </summary>
    public static Dictionary<string, string> ConvertToDomainFormat(DeserializedMemoryMetadata? metadata)
    {
        if (metadata == null)
            return [];

        var result = new Dictionary<string, string>();

        foreach (var key in metadata.Keys)
        {
            var value = metadata.Get<object>(key);
            if (value != null)
            {
                // Use invariant culture for numeric types to ensure consistent formatting
                result[key] = value switch
                {
                    float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    decimal dec => dec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => value.ToString() ?? string.Empty
                };
            }
        }

        return result;
    }
}

/// <summary>
/// Serializable memory metadata for persistence.
/// </summary>
[TypedDictionary(typeof(MemoryMetadataValue), EmitGenericAdd = false)]
public sealed partial class SerializableMemoryMetadata
{
    /// <summary>Converts the metadata to a plain dictionary.</summary>
    /// <returns>A dictionary of key-value pairs representing the metadata.</returns>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in _items)
        {
            result[kvp.Key] = kvp.Value.RawValue;
        }
        return result;
    }

    /// <summary>Builder for constructing <see cref="SerializableMemoryMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Adds the creation timestamp.</summary>
        /// <param name="createdAt">The creation timestamp.</param>
        /// <returns>This builder instance.</returns>
        [DictionaryEntry("createdAt")]
        public partial Builder AddCreatedAt(DateTime createdAt);

        /// <summary>Adds the source identifier.</summary>
        /// <param name="source">The source identifier.</param>
        /// <returns>This builder instance.</returns>
        [DictionaryEntry("source")]
        public partial Builder AddSource(string source);

        /// <summary>Adds the relevance score.</summary>
        /// <param name="relevance">The relevance score.</param>
        /// <returns>This builder instance.</returns>
        [DictionaryEntry("relevance")]
        public partial Builder AddRelevance(float relevance);

        /// <summary>Adds the access count.</summary>
        /// <param name="accessCount">The number of times the memory has been accessed.</param>
        /// <returns>This builder instance.</returns>
        [DictionaryEntry("accessCount")]
        public partial Builder AddAccessCount(int accessCount);

        /// <summary>Adds the last accessed timestamp.</summary>
        /// <param name="lastAccessedAt">The last access timestamp.</param>
        /// <returns>This builder instance.</returns>
        [DictionaryEntry("lastAccessedAt")]
        public partial Builder AddLastAccessedAt(DateTime lastAccessedAt);

        /// <summary>Adds tags associated with the memory.</summary>
        /// <param name="tags">The tags to associate.</param>
        /// <returns>This builder instance.</returns>
        public Builder AddTags(string[] tags)
        {
            _items["tags"] = MemoryMetadataValue.From(string.Join(",", tags));
            return this;
        }

        /// <summary>Adds the creator identifier.</summary>
        /// <param name="createdBy">The creator identifier.</param>
        /// <returns>This builder instance.</returns>
        [DictionaryEntry("createdBy")]
        public partial Builder AddCreatedBy(string createdBy);

        /// <summary>Adds a custom property.</summary>
        /// <param name="key">The property key.</param>
        /// <param name="value">The property value.</param>
        /// <returns>This builder instance.</returns>
        [DictionaryEntry("custom_{0}")]
        public partial Builder AddCustomProperty(string key, string value);
    }
}

/// <summary>
/// Deserialized memory metadata from persistence.
/// </summary>
public sealed class DeserializedMemoryMetadata
{
    private readonly ImmutableDictionary<string, MemoryMetadataValue> _data;

    private DeserializedMemoryMetadata(ImmutableDictionary<string, MemoryMetadataValue> data)
    {
        _data = data ?? [];
    }

    /// <summary>Gets an empty <see cref="DeserializedMemoryMetadata"/> instance.</summary>
    public static DeserializedMemoryMetadata Empty => new([]);

    /// <summary>Gets a typed value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The metadata key.</param>
    /// <returns>The typed value, or default if not found.</returns>
    public T? Get<T>(string key)
    {
        if (!_data.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>Gets all metadata keys.</summary>
    public IEnumerable<string> Keys => _data.Keys;

    /// <summary>Creates a <see cref="DeserializedMemoryMetadata"/> from a dictionary.</summary>
    /// <param name="dictionary">The source dictionary.</param>
    /// <returns>The deserialized metadata.</returns>
    public static DeserializedMemoryMetadata FromDictionary(Dictionary<string, object>? dictionary)
    {
        if (dictionary == null || dictionary.Count == 0)
            return Empty;

        var data = new Dictionary<string, MemoryMetadataValue>();
        foreach (var kvp in dictionary)
        {
            data[kvp.Key] = MemoryMetadataValue.From(kvp.Value);
        }

        return new DeserializedMemoryMetadata(data.ToImmutableDictionary());
    }
}

/// <summary>
/// Memory metadata value wrapper.
/// </summary>
public sealed class MemoryMetadataValue
{
    private readonly object _value;
    private readonly Type _type;

    private MemoryMetadataValue(object value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
    }

    /// <summary>Gets the value converted to the specified type.</summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <returns>The converted value.</returns>
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
                $"Cannot convert memory metadata value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw underlying value.</summary>
    public object RawValue => _value;

    /// <summary>Gets the type of the underlying value.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a <see cref="MemoryMetadataValue"/> from the specified object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="MemoryMetadataValue"/> wrapping the value.</returns>
    public static MemoryMetadataValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new MemoryMetadataValue(value, value.GetType());
    }
}
