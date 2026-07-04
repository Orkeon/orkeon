using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Domain.Memory.ValueObjects;

/// <summary>
/// Strongly typed metadata for episodic memory.
/// </summary>
[TypedDictionary(typeof(EpisodicMetadataValue), CacheEmpty = true)]
public sealed partial class EpisodicMemoryMetadata : IEquatable<EpisodicMemoryMetadata>
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

    /// <summary>Creates an <see cref="EpisodicMemoryMetadata"/> from a dictionary.</summary>
    /// <param name="dictionary">The source dictionary, or <see langword="null"/> for empty.</param>
    /// <returns>A new <see cref="EpisodicMemoryMetadata"/> instance.</returns>
    public static EpisodicMemoryMetadata FromDictionary(IDictionary<string, object>? dictionary)
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
    public bool Equals(EpisodicMemoryMetadata? other)
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
    public override bool Equals(object? obj) => Equals(obj as EpisodicMemoryMetadata);

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

    /// <summary>Builder for constructing <see cref="EpisodicMemoryMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets the context description.</summary>
        /// <param name="context">The context text.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("context")]
        public partial Builder AddContext(string context);

        /// <summary>Sets the list of tools used.</summary>
        /// <param name="tools">The tool names.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("tools_used")]
        public partial Builder AddToolsUsed(string[] tools);

        /// <summary>Sets the list of collaborating agents.</summary>
        /// <param name="collaborators">The collaborator identifiers.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("collaborators")]
        public partial Builder AddCollaborators(string[] collaborators);

        /// <summary>Sets the success score.</summary>
        /// <param name="score">The success score (0.0–1.0).</param>
        /// <returns>This builder for chaining.</returns>
        public Builder AddSuccessScore(double score)
        {
            if (score < 0.0 || score > 1.0)
                throw new ArgumentOutOfRangeException(nameof(score),
                    $"SuccessScore must be between 0.0 and 1.0, but was {score}.");
            _items["success_score"] = EpisodicMetadataValue.From(score);
            return this;
        }

        /// <summary>Sets the execution environment.</summary>
        /// <param name="environment">The environment name.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("environment")]
        public partial Builder AddEnvironment(string environment);

        /// <summary>Sets the iteration count.</summary>
        /// <param name="count">The number of iterations.</param>
        /// <returns>This builder for chaining.</returns>
        public Builder AddIterationCount(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            _items["iteration_count"] = EpisodicMetadataValue.From(count);
            return this;
        }
    }
}

/// <summary>
/// Strongly typed event data for episode events.
/// </summary>
[TypedDictionary(typeof(EpisodicMetadataValue), CacheEmpty = true)]
public sealed partial class EpisodeEventData
{
    /// <summary>Gets a typed value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The data key.</param>
    /// <returns>The typed value, or <see langword="default"/> if not found.</returns>
    public T? Get<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>Converts this instance to a <see cref="Dictionary{TKey,TValue}"/> of raw values.</summary>
    /// <returns>A dictionary of raw data values.</returns>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in _items)
        {
            result[kvp.Key] = kvp.Value.RawValue;
        }
        return result;
    }

    /// <summary>Creates an <see cref="EpisodeEventData"/> from a dictionary.</summary>
    /// <param name="dictionary">The source dictionary, or <see langword="null"/> for empty.</param>
    /// <returns>A new <see cref="EpisodeEventData"/> instance.</returns>
    public static EpisodeEventData FromDictionary(IDictionary<string, object>? dictionary)
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

    /// <summary>Builder for constructing <see cref="EpisodeEventData"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets the event input.</summary>
        /// <param name="input">The input data.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("input")]
        public partial Builder AddInput(string input);

        /// <summary>Sets the event output.</summary>
        /// <param name="output">The output data.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("output")]
        public partial Builder AddOutput(string output);

        /// <summary>Sets the event duration.</summary>
        /// <param name="duration">The duration of the event.</param>
        /// <returns>This builder for chaining.</returns>
        public Builder AddDuration(TimeSpan duration)
        {
            _items["duration_ms"] = EpisodicMetadataValue.From(duration.TotalMilliseconds);
            return this;
        }

        /// <summary>Sets an error message.</summary>
        /// <param name="error">The error description.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("error")]
        public partial Builder AddError(string error);

        /// <summary>Sets the tool name involved in the event.</summary>
        /// <param name="toolName">The tool name.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("tool_name")]
        public partial Builder AddToolName(string toolName);

        /// <summary>Sets the agent role involved in the event.</summary>
        /// <param name="role">The agent role.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("agent_role")]
        public partial Builder AddAgentRole(string role);

        /// <summary>Sets the confidence score.</summary>
        /// <param name="confidence">The confidence value (0.0–1.0).</param>
        /// <returns>This builder for chaining.</returns>
        public Builder AddConfidence(double confidence)
        {
            if (confidence < 0.0 || confidence > 1.0)
                throw new ArgumentOutOfRangeException(nameof(confidence),
                    $"Confidence must be between 0.0 and 1.0, but was {confidence}.");
            _items["confidence"] = EpisodicMetadataValue.From(confidence);
            return this;
        }
    }
}

/// <summary>
/// Value wrapper for episodic metadata.
/// </summary>
public sealed class EpisodicMetadataValue
{
    private readonly object _value;
    private readonly Type _type;

    private EpisodicMetadataValue(object value, Type type)
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
                $"Cannot convert episodic metadata value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw underlying value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the value's type.</summary>
    public Type ValueType => _type;

    /// <summary>Creates an <see cref="EpisodicMetadataValue"/> from the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="EpisodicMetadataValue"/>.</returns>
    public static EpisodicMetadataValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new EpisodicMetadataValue(value, value.GetType());
    }
}
