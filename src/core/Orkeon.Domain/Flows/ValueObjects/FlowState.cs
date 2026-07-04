using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Domain.Flows.ValueObjects;

/// <summary>
/// Strongly typed flow state for flow execution.
/// </summary>
[TypedDictionary(typeof(FlowStateValue), CacheEmpty = true, EmitBuilderFrom = true)]
public sealed partial class FlowState : IEquatable<FlowState>
{
    /// <summary>Gets a typed state value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The state key.</param>
    /// <returns>The typed value, or <see langword="default"/> if not found.</returns>
    public T? Get<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>Gets a required typed state value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The state key.</param>
    /// <returns>The typed value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the key does not exist.</exception>
    public T GetRequired<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            throw new KeyNotFoundException($"Required flow state key '{key}' not found");

        return value.GetValue<T>();
    }

    /// <summary>Returns whether a state entry exists for the given key.</summary>
    /// <param name="key">The state key.</param>
    /// <returns><see langword="true"/> if the key exists; otherwise <see langword="false"/>.</returns>
    public bool ContainsKey(string key) => _items.ContainsKey(key);

    /// <summary>Gets all state keys.</summary>
    public IEnumerable<string> Keys => _items.Keys;

    /// <summary>Gets the number of state entries.</summary>
    public int Count => _items.Count;

    /// <summary>Returns a new instance with the specified state value set.</summary>
    /// <param name="key">The state key.</param>
    /// <param name="value">The value.</param>
    /// <returns>A new <see cref="FlowState"/> with the value set.</returns>
    public FlowState Set(string key, object value)
    {
        var newState = _items.SetItem(key, FlowStateValue.From(value));
        return new FlowState(newState);
    }

    /// <summary>Returns a new instance with multiple state values set.</summary>
    /// <param name="items">The key-value pairs to set.</param>
    /// <returns>A new <see cref="FlowState"/> with the values set.</returns>
    public FlowState SetMultiple(IEnumerable<KeyValuePair<string, object>> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var builder = _items.ToBuilder();
        foreach (var item in items)
        {
            builder[item.Key] = FlowStateValue.From(item.Value);
        }
        return new FlowState(builder.ToImmutable());
    }

    /// <summary>Returns a new instance with the specified key removed.</summary>
    /// <param name="key">The state key to remove.</param>
    /// <returns>A new <see cref="FlowState"/> without the key.</returns>
    public FlowState Remove(string key)
    {
        var newState = _items.Remove(key);
        return new FlowState(newState);
    }

    /// <summary>Converts this instance to a <see cref="Dictionary{TKey,TValue}"/> of raw values.</summary>
    /// <returns>A dictionary of raw state values.</returns>
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
    public bool Equals(FlowState? other)
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
    public override bool Equals(object? obj) => Equals(obj as FlowState);

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

    /// <summary>Creates a <see cref="FlowState"/> from a dictionary.</summary>
    /// <param name="dictionary">The source dictionary, or <see langword="null"/> for empty.</param>
    /// <returns>A new <see cref="FlowState"/> instance.</returns>
    public static FlowState FromDictionary(Dictionary<string, object>? dictionary)
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

    /// <summary>Returns a new instance with the specified updates merged in.</summary>
    /// <param name="updates">The key-value pairs to merge.</param>
    /// <returns>A new <see cref="FlowState"/> with the updates applied.</returns>
    public FlowState Merge(Dictionary<string, object> updates)
    {
        if (updates == null || updates.Count == 0)
            return this;

        var builder = CreateBuilderFrom(this);
        foreach (var kvp in updates)
        {
            builder.Add(kvp.Key, kvp.Value);
        }
        return builder.Build();
    }

    /// <summary>Sets a flow variable under the "variables." namespace.</summary>
    /// <param name="key">The variable key.</param>
    /// <param name="value">The variable value.</param>
    /// <returns>A new <see cref="FlowState"/> with the variable set.</returns>
    public FlowState SetVariable(string key, object value) => Set($"variables.{key}", value);

    /// <summary>Gets a flow variable by key from the "variables." namespace.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The variable key.</param>
    /// <returns>The typed value, or <see langword="null"/> if not found.</returns>
    public T? GetVariable<T>(string key) where T : class => Get<T>($"variables.{key}");

    /// <summary>Sets a shared state value under the "shared." namespace.</summary>
    /// <param name="key">The shared state key.</param>
    /// <param name="value">The value.</param>
    /// <returns>A new <see cref="FlowState"/> with the shared state set.</returns>
    public FlowState SetSharedState(string key, object value) => Set($"shared.{key}", value);

    /// <summary>Gets a shared state value by key from the "shared." namespace.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The shared state key.</param>
    /// <returns>The typed value, or <see langword="null"/> if not found.</returns>
    public T? GetSharedState<T>(string key) where T : class => Get<T>($"shared.{key}");

    /// <summary>Creates a snapshot of the current state.</summary>
    /// <param name="label">Optional label for the snapshot.</param>
    /// <returns>A <see cref="FlowStateSnapshot"/> of the current state.</returns>
    public FlowStateSnapshot CreateSnapshot(string label = "")
    {
        return new FlowStateSnapshot
        {
            Label = label,
            Timestamp = DateTime.UtcNow,
            State = ToDictionary()
        };
    }

    /// <summary>Creates a snapshot and saves it in the state under "snapshots.{label}".</summary>
    /// <param name="label">Optional label for the snapshot.</param>
    /// <returns>A new <see cref="FlowState"/> with the snapshot saved.</returns>
    public FlowState SaveSnapshot(string label = "")
    {
        var snapshot = CreateSnapshot(label);
        return Set($"snapshots.{label}", snapshot);
    }

    /// <summary>Builder for constructing <see cref="FlowState"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets the current step name.</summary>
        /// <param name="stepName">The step name.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("current_step")]
        public partial Builder AddCurrentStep(string stepName);

        /// <summary>Sets the list of completed steps.</summary>
        /// <param name="steps">The completed step names.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("completed_steps")]
        public partial Builder AddCompletedSteps(IReadOnlyList<string> steps);

        /// <summary>Sets the flow start time.</summary>
        /// <param name="startTime">The start time.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("start_time")]
        public partial Builder AddStartTime(DateTime startTime);

        /// <summary>Sets the iteration count.</summary>
        /// <param name="count">The number of iterations.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("iteration_count")]
        public partial Builder AddIterationCount(int count);

        /// <summary>Sets an error message.</summary>
        /// <param name="error">The error description.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("error")]
        public partial Builder AddError(string error);

        /// <summary>Sets the flow output.</summary>
        /// <param name="output">The output value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("output")]
        public partial Builder AddOutput(object output);

        /// <summary>Sets the user input.</summary>
        /// <param name="input">The user input text.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("user_input")]
        public partial Builder AddUserInput(string input);
    }
}

/// <summary>
/// Represents a snapshot of flow state at a specific point in time.
/// </summary>
public sealed record FlowStateSnapshot
{
    /// <summary>Gets the snapshot label.</summary>
    public string Label { get; init; } = string.Empty;
    /// <summary>Gets the timestamp when this snapshot was taken.</summary>
    public DateTime Timestamp { get; init; }
    /// <summary>Gets the state data captured in this snapshot.</summary>
    public IReadOnlyDictionary<string, object> State { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Value wrapper for flow state values.
/// </summary>
public sealed class FlowStateValue
{
    private readonly object _value;
    private readonly Type _type;

    private FlowStateValue(object value, Type type)
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
                $"Cannot convert flow state value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw underlying value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the value's type.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a <see cref="FlowStateValue"/> from the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="FlowStateValue"/>.</returns>
    public static FlowStateValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new FlowStateValue(value, value.GetType());
    }
}
