using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>
/// Strongly typed context for task execution.
/// </summary>
/// <remarks>
/// The dictionary-wrapper plumbing (private constructor, <see cref="Empty"/>,
/// <see cref="CreateBuilder"/>, <see cref="CreateBuilderFrom"/> and the
/// <see cref="Builder"/> shell) is emitted by the Orkeon.Generators source generator.
/// </remarks>
[TypedDictionary(typeof(TaskContextValue), CacheEmpty = true, EmitBuilderFrom = true)]
public sealed partial class TaskContext : IEquatable<TaskContext>
{
    /// <summary>Gets a typed context value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The context key.</param>
    /// <returns>The typed value, or <see langword="default"/> if not found.</returns>
    public T? Get<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>Returns whether a context entry exists for the given key.</summary>
    /// <param name="key">The context key.</param>
    /// <returns><see langword="true"/> if the key exists; otherwise <see langword="false"/>.</returns>
    public bool ContainsKey(string key) => _items.ContainsKey(key);

    /// <summary>Gets all context keys.</summary>
    public IEnumerable<string> Keys => _items.Keys;

    /// <summary>Gets the number of context entries.</summary>
    public int Count => _items.Count;

    /// <summary>Returns a new instance with the specified context value set.</summary>
    /// <param name="key">The context key.</param>
    /// <param name="value">The value.</param>
    /// <returns>A new <see cref="TaskContext"/> with the value set.</returns>
    public TaskContext Set(string key, object value)
    {
        var newContext = _items.SetItem(key, TaskContextValue.From(value));
        return new TaskContext(newContext);
    }

    /// <summary>Returns a new instance with multiple context values set.</summary>
    /// <param name="items">The key-value pairs to set.</param>
    /// <returns>A new <see cref="TaskContext"/> with the values set.</returns>
    public TaskContext SetMultiple(IEnumerable<KeyValuePair<string, object>> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var builder = _items.ToBuilder();
        foreach (var item in items)
        {
            builder[item.Key] = TaskContextValue.From(item.Value);
        }
        return new TaskContext(builder.ToImmutable());
    }

    /// <summary>Returns a new instance with the specified key removed.</summary>
    /// <param name="key">The context key to remove.</param>
    /// <returns>A new <see cref="TaskContext"/> without the key.</returns>
    public TaskContext Remove(string key)
    {
        var newContext = _items.Remove(key);
        return new TaskContext(newContext);
    }

    /// <summary>Converts this instance to a read-only dictionary of raw values.</summary>
    /// <returns>A read-only dictionary of raw context values.</returns>
    public IReadOnlyDictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in _items)
        {
            result[kvp.Key] = kvp.Value.RawValue;
        }
        return result;
    }

    /// <inheritdoc />
    public bool Equals(TaskContext? other)
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
    public override bool Equals(object? obj) => Equals(obj as TaskContext);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_items.Count);
        foreach (var kvp in _items.OrderBy(k => k.Key))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value.RawValue);
        }
        return hash.ToHashCode();
    }

    /// <summary>Builder for constructing <see cref="TaskContext"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets a named input value.</summary>
        /// <param name="name">The input name.</param>
        /// <param name="value">The input value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("input.{0}")]
        public partial Builder AddInput(string name, object value);

        /// <summary>Sets a named output value.</summary>
        /// <param name="name">The output name.</param>
        /// <param name="value">The output value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("output.{0}")]
        public partial Builder AddOutput(string name, object value);

        /// <summary>Sets the previous task result.</summary>
        /// <param name="result">The previous result value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("previous_result")]
        public partial Builder AddPreviousResult(object result);

        /// <summary>Sets context data for a specific agent.</summary>
        /// <param name="agentId">The agent identifier.</param>
        /// <param name="context">The agent context data.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("agent.{0}")]
        public partial Builder AddAgentContext(string agentId, object context);

        /// <summary>Sets data for a specific tool.</summary>
        /// <param name="toolName">The tool name.</param>
        /// <param name="toolData">The tool data.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("tool.{0}")]
        public partial Builder AddTool(string toolName, object toolData);

        /// <summary>Sets a memory entry.</summary>
        /// <param name="memoryKey">The memory key.</param>
        /// <param name="memoryData">The memory data.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("memory.{0}")]
        public partial Builder AddMemory(string memoryKey, object memoryData);

        /// <summary>Sets a crew-level context value.</summary>
        /// <param name="key">The crew context key.</param>
        /// <param name="value">The value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("crew.{0}")]
        public partial Builder AddCrewContext(string key, object value);
    }

    /// <summary>Creates a <see cref="TaskContext"/> from a dictionary.</summary>
    /// <param name="dictionary">The source dictionary, or <see langword="null"/> for empty.</param>
    /// <returns>A new <see cref="TaskContext"/> instance.</returns>
    public static TaskContext FromDictionary(Dictionary<string, object>? dictionary)
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
}

/// <summary>
/// Value wrapper for task context values.
/// </summary>
public sealed class TaskContextValue
{
    private readonly object _value;
    private readonly Type _type;

    private TaskContextValue(object value, Type type)
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
                $"Cannot convert task context value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw underlying value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the value's type.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a <see cref="TaskContextValue"/> from the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="TaskContextValue"/>.</returns>
    public static TaskContextValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new TaskContextValue(value, value.GetType());
    }
}
