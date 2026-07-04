using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Domain.Crew.ValueObjects;

/// <summary>
/// Strongly typed data for crew inputs.
/// </summary>
[TypedDictionary(typeof(CrewInputValue), CacheEmpty = true, EmitBuilderFrom = true)]
public sealed partial class CrewInputData : IEquatable<CrewInputData>
{
    /// <summary>Gets a typed value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The input key.</param>
    /// <returns>The typed value, or <see langword="default"/> if not found.</returns>
    public T? GetValue<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>Gets a raw object value by key.</summary>
    /// <param name="key">The input key.</param>
    /// <returns>The raw value, or <see langword="null"/> if not found.</returns>
    public object? GetObject(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return null;

        return value.RawValue;
    }

    /// <summary>Returns whether a value exists for the given key.</summary>
    /// <param name="key">The input key.</param>
    /// <returns><see langword="true"/> if the key exists; otherwise <see langword="false"/>.</returns>
    public bool HasValue(string key) => _items.ContainsKey(key);

    /// <summary>Gets all input keys.</summary>
    public IEnumerable<string> Keys => _items.Keys;

    /// <summary>Gets the number of input values.</summary>
    public int Count => _items.Count;

    /// <summary>Returns a new <see cref="CrewInputData"/> with the specified value set.</summary>
    /// <param name="key">The input key.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>A new instance with the value set.</returns>
    public CrewInputData SetValue(string key, object value)
    {
        var newData = _items.SetItem(key, CrewInputValue.From(value));
        return new CrewInputData(newData);
    }

    /// <summary>Returns a new <see cref="CrewInputData"/> with multiple values set.</summary>
    /// <param name="items">The key-value pairs to set.</param>
    /// <returns>A new instance with the values set.</returns>
    public CrewInputData SetMultiple(IEnumerable<KeyValuePair<string, object>> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var builder = _items.ToBuilder();
        foreach (var item in items)
        {
            builder[item.Key] = CrewInputValue.From(item.Value);
        }
        return new CrewInputData(builder.ToImmutable());
    }

    /// <summary>Returns a new <see cref="CrewInputData"/> with the specified key removed.</summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>A new instance without the key.</returns>
    public CrewInputData Remove(string key)
    {
        var newData = _items.Remove(key);
        return new CrewInputData(newData);
    }

    /// <summary>Converts this instance to a <see cref="Dictionary{TKey,TValue}"/>.</summary>
    /// <returns>A dictionary of raw values.</returns>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in _items)
        {
            result[kvp.Key] = kvp.Value.RawValue;
        }
        return result;
    }

    /// <summary>Creates a <see cref="CrewInputData"/> from a dictionary.</summary>
    /// <param name="dictionary">The source dictionary, or <see langword="null"/> for empty.</param>
    /// <returns>A new <see cref="CrewInputData"/> instance.</returns>
    public static CrewInputData FromDictionary(Dictionary<string, object>? dictionary)
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
    public bool Equals(CrewInputData? other)
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
    public override bool Equals(object? obj) => Equals(obj as CrewInputData);

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

    /// <summary>Builder for constructing <see cref="CrewInputData"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Adds a prompt value.</summary>
        /// <param name="prompt">The prompt text.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("prompt")]
        public partial Builder AddPrompt(string prompt);

        /// <summary>Adds a context value.</summary>
        /// <param name="context">The context object.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("context")]
        public partial Builder AddContext(object context);

        /// <summary>Adds a goal value.</summary>
        /// <param name="goal">The goal text.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("goal")]
        public partial Builder AddGoal(string goal);

        /// <summary>Adds constraints.</summary>
        /// <param name="constraints">The list of constraints.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("constraints")]
        public partial Builder AddConstraints(IReadOnlyList<string> constraints);

        /// <summary>Adds a maximum iterations value.</summary>
        /// <param name="iterations">The maximum number of iterations.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("max_iterations")]
        public partial Builder AddMaxIterations(int iterations);

        /// <summary>Adds a time limit value.</summary>
        /// <param name="timeLimit">The time limit.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("time_limit")]
        public partial Builder AddTimeLimit(TimeSpan timeLimit);

        /// <summary>Adds a memory context entry.</summary>
        /// <param name="memoryKey">The memory key suffix.</param>
        /// <param name="memoryData">The memory data.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("memory.{0}")]
        public partial Builder AddMemoryContext(string memoryKey, object memoryData);
    }
}

/// <summary>
/// Value wrapper for crew input values.
/// </summary>
public sealed class CrewInputValue
{
    private readonly object _value;
    private readonly Type _type;

    private CrewInputValue(object value, Type type)
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
                $"Cannot convert crew input value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw underlying value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the value's type.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a <see cref="CrewInputValue"/> from the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="CrewInputValue"/>.</returns>
    public static CrewInputValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new CrewInputValue(value, value.GetType());
    }
}
