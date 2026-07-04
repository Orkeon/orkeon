using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Domain.Common;
using Orkeon.Generators;

namespace Orkeon.Domain.Flows.ValueObjects;

/// <summary>
/// Strongly typed parameters for flow steps.
/// </summary>
[TypedDictionary(typeof(FlowStepParameterValue), CacheEmpty = true)]
public sealed partial class FlowStepParameters : IEquatable<FlowStepParameters>
{
    /// <summary>Gets a typed parameter value by key.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The parameter key.</param>
    /// <returns>The typed value, or <see langword="default"/> if not found.</returns>
    public T? Get<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>Returns whether a parameter exists for the given key.</summary>
    /// <param name="key">The parameter key.</param>
    /// <returns><see langword="true"/> if the key exists; otherwise <see langword="false"/>.</returns>
    public bool ContainsKey(string key) => _items.ContainsKey(key);

    /// <summary>Gets all parameter keys.</summary>
    public IEnumerable<string> Keys => _items.Keys;

    /// <summary>Gets the number of parameters.</summary>
    public int Count => _items.Count;

    /// <summary>Returns a new instance with the specified parameter set.</summary>
    /// <param name="key">The parameter key.</param>
    /// <param name="value">The value.</param>
    /// <returns>A new <see cref="FlowStepParameters"/> with the value set.</returns>
    public FlowStepParameters Set(string key, object value)
    {
        var newParams = _items.SetItem(key, FlowStepParameterValue.From(value));
        return new FlowStepParameters(newParams);
    }

    /// <summary>Converts this instance to a <see cref="Dictionary{TKey,TValue}"/> of raw values.</summary>
    /// <returns>A dictionary of raw parameter values.</returns>
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
    public bool Equals(FlowStepParameters? other)
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
    public override bool Equals(object? obj) => Equals(obj as FlowStepParameters);

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

    /// <summary>Creates a <see cref="FlowStepParameters"/> from a dictionary.</summary>
    /// <param name="dictionary">The source dictionary, or <see langword="null"/> for empty.</param>
    /// <returns>A new <see cref="FlowStepParameters"/> instance.</returns>
    public static FlowStepParameters FromDictionary(Dictionary<string, object>? dictionary)
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

    /// <summary>Builder for constructing <see cref="FlowStepParameters"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets an input parameter.</summary>
        /// <param name="inputName">The input name.</param>
        /// <param name="value">The input value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("input.{0}")]
        public partial Builder AddInput(string inputName, object value);

        /// <summary>Sets the step condition.</summary>
        /// <param name="condition">The condition expression.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("condition")]
        public partial Builder AddCondition(string condition);

        /// <summary>Sets the maximum loop iterations.</summary>
        /// <param name="maxIterations">The maximum number of iterations.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("max_iterations")]
        public partial Builder AddLoop(int maxIterations);

        /// <summary>Sets the agent ID for this step.</summary>
        /// <param name="agentId">The agent identifier.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("agent_id")]
        public partial Builder AddAgent(AgentId agentId);

        /// <summary>Sets the task ID for this step.</summary>
        /// <param name="taskId">The task identifier.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("task_id")]
        public partial Builder AddTask(TaskId taskId);

        /// <summary>Sets the tool name for this step.</summary>
        /// <param name="toolName">The tool name.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("tool")]
        public partial Builder AddTool(string toolName);
    }
}

/// <summary>
/// Strongly typed metadata for flow steps.
/// </summary>
[TypedDictionary(typeof(FlowStepMetadataValue), CacheEmpty = true)]
public sealed partial class FlowStepMetadata : IEquatable<FlowStepMetadata>
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

    /// <summary>Returns whether a metadata entry exists for the given key.</summary>
    /// <param name="key">The metadata key.</param>
    /// <returns><see langword="true"/> if the key exists; otherwise <see langword="false"/>.</returns>
    public bool ContainsKey(string key) => _items.ContainsKey(key);

    /// <summary>Gets all metadata keys.</summary>
    public IEnumerable<string> Keys => _items.Keys;

    /// <summary>Gets the number of metadata entries.</summary>
    public int Count => _items.Count;

    /// <summary>Returns a new instance with the specified metadata value set.</summary>
    /// <param name="key">The metadata key.</param>
    /// <param name="value">The value.</param>
    /// <returns>A new <see cref="FlowStepMetadata"/> with the value set.</returns>
    public FlowStepMetadata Set(string key, object value)
    {
        var newMetadata = _items.SetItem(key, FlowStepMetadataValue.From(value));
        return new FlowStepMetadata(newMetadata);
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
    public bool Equals(FlowStepMetadata? other)
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
    public override bool Equals(object? obj) => Equals(obj as FlowStepMetadata);

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

    /// <summary>Creates a <see cref="FlowStepMetadata"/> from a dictionary.</summary>
    /// <param name="dictionary">The source dictionary, or <see langword="null"/> for empty.</param>
    /// <returns>A new <see cref="FlowStepMetadata"/> instance.</returns>
    public static FlowStepMetadata FromDictionary(Dictionary<string, object>? dictionary)
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

    /// <summary>Builder for constructing <see cref="FlowStepMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Sets the description.</summary>
        /// <param name="description">The description text.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("description")]
        public partial Builder AddDescription(string description);

        /// <summary>Sets the category.</summary>
        /// <param name="category">The category name.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("category")]
        public partial Builder AddCategory(string category);

        /// <summary>Sets the priority.</summary>
        /// <param name="priority">The priority value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("priority")]
        public partial Builder AddPriority(int priority);

        /// <summary>Sets the tags.</summary>
        /// <param name="tags">The list of tags.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("tags")]
        public partial Builder AddTags(IReadOnlyList<string> tags);

        /// <summary>Sets the creation timestamp.</summary>
        /// <param name="createdAt">The creation date and time.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("created_at")]
        public partial Builder AddCreatedAt(DateTime createdAt);
    }
}

/// <summary>
/// Value wrapper for flow step parameters.
/// </summary>
public sealed class FlowStepParameterValue
{
    private readonly object _value;
    private readonly Type _type;

    private FlowStepParameterValue(object value, Type type)
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
                $"Cannot convert flow step parameter value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw underlying value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the value's type.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a <see cref="FlowStepParameterValue"/> from the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="FlowStepParameterValue"/>.</returns>
    public static FlowStepParameterValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new FlowStepParameterValue(value, value.GetType());
    }
}

/// <summary>
/// Value wrapper for flow step metadata.
/// </summary>
public sealed class FlowStepMetadataValue
{
    private readonly object _value;
    private readonly Type _type;

    private FlowStepMetadataValue(object value, Type type)
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
                $"Cannot convert flow step metadata value of type {_type.Name} to {typeof(T).Name}", ex);
        }
    }

    /// <summary>Gets the raw underlying value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the value's type.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a <see cref="FlowStepMetadataValue"/> from the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="FlowStepMetadataValue"/>.</returns>
    public static FlowStepMetadataValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new FlowStepMetadataValue(value, value.GetType());
    }
}
