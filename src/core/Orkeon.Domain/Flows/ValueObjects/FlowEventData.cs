using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Domain.Common;
using Orkeon.Generators;

namespace Orkeon.Domain.Flows.ValueObjects;

/// <summary>
/// Strongly typed data for flow events.
/// </summary>
[TypedDictionary(typeof(FlowEventDataValue), CacheEmpty = true)]
public sealed partial class FlowEventData : IEquatable<FlowEventData>
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

    /// <summary>Returns whether a value exists for the given key.</summary>
    /// <param name="key">The data key.</param>
    /// <returns><see langword="true"/> if the key exists; otherwise <see langword="false"/>.</returns>
    public bool ContainsKey(string key) => _items.ContainsKey(key);

    /// <summary>Gets all data keys.</summary>
    public IEnumerable<string> Keys => _items.Keys;

    /// <summary>Gets the number of data entries.</summary>
    public int Count => _items.Count;

    /// <summary>Returns a new instance with the specified value set.</summary>
    /// <param name="key">The data key.</param>
    /// <param name="value">The value.</param>
    /// <returns>A new <see cref="FlowEventData"/> with the value set.</returns>
    public FlowEventData Set(string key, object value)
    {
        var newData = _items.SetItem(key, FlowEventDataValue.From(value));
        return new FlowEventData(newData);
    }

    /// <summary>Converts this instance to a <see cref="Dictionary{TKey,TValue}"/> of raw values.</summary>
    /// <returns>A dictionary of raw event data values.</returns>
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
    public bool Equals(FlowEventData? other)
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
    public override bool Equals(object? obj) => Equals(obj as FlowEventData);

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

    /// <summary>Creates a <see cref="FlowEventData"/> from a dictionary.</summary>
    /// <param name="dictionary">The source dictionary, or <see langword="null"/> for empty.</param>
    /// <returns>A new <see cref="FlowEventData"/> instance.</returns>
    public static FlowEventData FromDictionary(Dictionary<string, object>? dictionary)
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

    /// <summary>Builder for constructing <see cref="FlowEventData"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets the step result.</summary>
        /// <param name="result">The step result object.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("step_result")]
        public partial Builder AddStepResult(object result);

        /// <summary>Sets the previous step ID.</summary>
        /// <param name="stepId">The previous step identifier.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("previous_step_id")]
        public partial Builder AddPreviousStepId(FlowStepId stepId);

        /// <summary>Sets the next step ID.</summary>
        /// <param name="stepId">The next step identifier.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("next_step_id")]
        public partial Builder AddNextStepId(FlowStepId stepId);

        /// <summary>Sets the event duration.</summary>
        /// <param name="duration">The duration of the event.</param>
        /// <returns>This builder for chaining.</returns>
        public Builder AddDuration(TimeSpan duration)
        {
            _items["duration"] = FlowEventDataValue.From(duration.TotalMilliseconds);
            return this;
        }

        /// <summary>Sets the retry count.</summary>
        /// <param name="count">The number of retries.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("retry_count")]
        public partial Builder AddRetryCount(int count);

        /// <summary>Sets a state snapshot.</summary>
        /// <param name="snapshot">The state snapshot.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("state_snapshot")]
        public partial Builder AddStateSnapshot(FlowStateSnapshot snapshot);

        /// <summary>Sets a descriptive message.</summary>
        /// <param name="message">The message text.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("message")]
        public partial Builder AddMessage(string message);
    }
}

/// <summary>
/// Value wrapper for flow event data.
/// </summary>
public sealed class FlowEventDataValue
{
    private readonly object _value;
    private readonly Type _type;

    private FlowEventDataValue(object value, Type type)
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
                $"Cannot convert flow event data value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw underlying value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the value's type.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a <see cref="FlowEventDataValue"/> from the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="FlowEventDataValue"/>.</returns>
    public static FlowEventDataValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new FlowEventDataValue(value, value.GetType());
    }
}
