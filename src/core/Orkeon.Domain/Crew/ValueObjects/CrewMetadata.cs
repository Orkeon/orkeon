using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Domain.Crew.ValueObjects;

/// <summary>
/// Strongly typed crew metadata container.
/// </summary>
[TypedDictionary(typeof(CrewMetadataValue), CacheEmpty = true)]
public sealed partial record CrewMetadata
{
    /// <summary>
    /// Canonical metadata key carrying the total token count measured during crew
    /// execution. Every process strategy propagates its token telemetry under this
    /// key so orchestrators can rebuild a real token usage (R10.8 / MAT-004).
    /// </summary>
    public const string TotalTokensKey = "totalTokens";

    /// <summary>
    /// Canonical metadata key carrying the prompt-side token count, when the
    /// underlying provider reported the prompt/completion split.
    /// </summary>
    public const string PromptTokensKey = "promptTokens";

    /// <summary>
    /// Canonical metadata key carrying the completion-side token count, when the
    /// underlying provider reported the prompt/completion split.
    /// </summary>
    public const string CompletionTokensKey = "completionTokens";

    /// <summary>
    /// Canonical metadata key carrying the prompt tokens served from the provider's
    /// cache, when cache telemetry was reported. The hit/miss pair partitions the
    /// prompt tokens — it is never additive to the totals (W-08).
    /// </summary>
    public const string CacheHitTokensKey = "cacheHitTokens";

    /// <summary>
    /// Canonical metadata key carrying the prompt tokens the provider had to compute,
    /// when cache telemetry was reported.
    /// </summary>
    public const string CacheMissTokensKey = "cacheMissTokens";

    /// <summary>Gets a metadata value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The metadata key.</param>
    /// <returns>The typed value, or <see langword="default"/> if not found.</returns>
    public T? Get<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>Gets a required metadata value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The metadata key.</param>
    /// <returns>The typed value.</returns>
    /// <exception cref="ArgumentException">Thrown if the key does not exist.</exception>
    public T GetRequired<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            throw new ArgumentException($"Required metadata '{key}' not found", nameof(key));

        return value.GetValue<T>();
    }

    /// <summary>Returns whether a metadata entry exists for the given key.</summary>
    /// <param name="key">The metadata key.</param>
    /// <returns><see langword="true"/> if the key exists; otherwise <see langword="false"/>.</returns>
    public bool Contains(string key) => _items.ContainsKey(key);

    /// <summary>Gets all metadata keys.</summary>
    public IEnumerable<string> Keys => _items.Keys;

    /// <summary>Gets the number of metadata entries.</summary>
    public int Count => _items.Count;

    /// <summary>Creates a <see cref="CrewMetadata"/> from a dictionary.</summary>
    /// <param name="dictionary">The source dictionary.</param>
    /// <returns>A new <see cref="CrewMetadata"/> instance.</returns>
    public static CrewMetadata FromDictionary(Dictionary<string, object> dictionary)
    {
        if (dictionary == null || dictionary.Count == 0)
            return Empty;

        var builder = new Builder();
        foreach (var kvp in dictionary)
        {
            builder.Add(kvp.Key, kvp.Value);
        }
        return builder.Build();
    }

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

    /// <inheritdoc />
    public bool Equals(CrewMetadata? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        if (_items.Count != other._items.Count) return false;

        foreach (var kvp in _items)
        {
            if (!other._items.TryGetValue(kvp.Key, out var otherValue))
                return false;
            if (!kvp.Value.RawValue.Equals(otherValue.RawValue))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var kvp in _items.OrderBy(x => x.Key))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value.RawValue);
        }
        return hash.ToHashCode();
    }

    /// <summary>Builder for constructing <see cref="CrewMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets the execution ID.</summary>
        /// <param name="executionId">The execution identifier.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("executionId")]
        public partial Builder AddExecutionId(string executionId);

        /// <summary>Sets the executor agent ID.</summary>
        /// <param name="agentId">The agent identifier.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("executorAgent")]
        public partial Builder AddExecutorAgent(string agentId);

        /// <summary>Sets the memory type used.</summary>
        /// <param name="memoryType">The memory type descriptor.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("memoryUsed")]
        public partial Builder AddMemoryUsed(string memoryType);

        /// <summary>Sets the LLM provider name.</summary>
        /// <param name="provider">The LLM provider name.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("llmProvider")]
        public partial Builder AddLlmProvider(string provider);

        /// <summary>Sets the total token count.</summary>
        /// <param name="tokens">The total number of tokens consumed.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry(TotalTokensKey)]
        public partial Builder AddTotalTokens(int tokens);

        /// <summary>Sets the prompt-side token count (provider-reported split).</summary>
        /// <param name="tokens">The number of prompt tokens consumed.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry(PromptTokensKey)]
        public partial Builder AddPromptTokens(int tokens);

        /// <summary>Sets the completion-side token count (provider-reported split).</summary>
        /// <param name="tokens">The number of completion tokens consumed.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry(CompletionTokensKey)]
        public partial Builder AddCompletionTokens(int tokens);

        /// <summary>Sets the cache-served prompt token count (provider-reported).</summary>
        /// <param name="tokens">The number of prompt tokens served from cache.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry(CacheHitTokensKey)]
        public partial Builder AddCacheHitTokens(long tokens);

        /// <summary>Sets the cache-missed prompt token count (provider-reported).</summary>
        /// <param name="tokens">The number of prompt tokens the provider computed.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry(CacheMissTokensKey)]
        public partial Builder AddCacheMissTokens(long tokens);

        /// <summary>Sets the total cost.</summary>
        /// <param name="cost">The total cost of the execution.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("totalCost")]
        public partial Builder AddTotalCost(double cost);

        /// <summary>Adds a tag to the metadata.</summary>
        /// <param name="tag">The tag name.</param>
        /// <returns>This builder for chaining.</returns>
        public Builder AddTag(string tag)
        {
            _items[$"tag_{tag}"] = CrewMetadataValue.From(true);
            return this;
        }

        /// <summary>Sets a timestamp entry.</summary>
        /// <param name="key">The metadata key.</param>
        /// <param name="timestamp">The timestamp value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry]
        public partial Builder AddTimestamp(string key, DateTime timestamp);

        /// <summary>Sets a duration entry.</summary>
        /// <param name="key">The metadata key.</param>
        /// <param name="duration">The duration value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry]
        public partial Builder AddDuration(string key, TimeSpan duration);
    }
}

/// <summary>
/// Represents a single crew metadata value.
/// </summary>
public sealed class CrewMetadataValue
{
    private readonly object _value;
    private readonly Type _type;

    private CrewMetadataValue(object value, Type type)
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
                $"Cannot convert crew metadata value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw underlying value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the value's type.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a <see cref="CrewMetadataValue"/> from the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="CrewMetadataValue"/>.</returns>
    public static CrewMetadataValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new CrewMetadataValue(value, value.GetType());
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj)) return true;
        if (obj is not CrewMetadataValue other) return false;
        return _value.Equals(other._value) && _type == other._type;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(_value, _type);
    }
}
