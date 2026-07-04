using System.Collections.Immutable;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Generators;

namespace Orkeon.Domain.Crew.ValueObjects;

/// <summary>
/// Strongly typed process outputs container.
/// </summary>
[TypedDictionary(typeof(ProcessOutputValue), CacheEmpty = true, EmitGenericAdd = false)]
public sealed partial class ProcessOutputs : IEquatable<ProcessOutputs>
{
    /// <summary>
    /// Gets an output value by key.
    /// </summary>
    public T? Get<T>(string key) where T : class
    {
        if (!_items.TryGetValue(key, out var output))
            return null;

        return output.GetValue<T>();
    }

    /// <summary>
    /// Gets a required output value.
    /// </summary>
    public T GetRequired<T>(string key) where T : class
    {
        if (!_items.TryGetValue(key, out var output))
            throw new ArgumentException($"Required output '{key}' not found", nameof(key));

        return output.GetValue<T>();
    }

    /// <summary>
    /// Checks if an output exists.
    /// </summary>
    public bool Contains(string key) => _items.ContainsKey(key);

    /// <summary>
    /// Gets all output keys.
    /// </summary>
    public IEnumerable<string> Keys => _items.Keys;

    /// <summary>
    /// Gets the count of outputs.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Creates a new ProcessOutputs with an added output.
    /// </summary>
    public ProcessOutputs With(string key, object value)
    {
        return new ProcessOutputs(_items.SetItem(key, ProcessOutputValue.From(value)));
    }

    /// <inheritdoc />
    public bool Equals(ProcessOutputs? other)
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
    public override bool Equals(object? obj) => Equals(obj as ProcessOutputs);

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

    /// <summary>Builder for constructing <see cref="ProcessOutputs"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>Adds a string output.</summary>
        /// <param name="key">The output key.</param>
        /// <param name="value">The string value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry]
        public partial Builder AddString(string key, string value);

        /// <summary>Adds an integer output.</summary>
        /// <param name="key">The output key.</param>
        /// <param name="value">The integer value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry]
        public partial Builder AddInt(string key, int value);

        /// <summary>Adds a double output.</summary>
        /// <param name="key">The output key.</param>
        /// <param name="value">The double value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry]
        public partial Builder AddDouble(string key, double value);

        /// <summary>Adds a boolean output.</summary>
        /// <param name="key">The output key.</param>
        /// <param name="value">The boolean value.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry]
        public partial Builder AddBool(string key, bool value);

        /// <summary>Adds an object output.</summary>
        /// <param name="key">The output key.</param>
        /// <param name="value">The object value.</param>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry]
        public partial Builder AddObject<T>(string key, T value) where T : class;

        /// <summary>Adds a task output keyed by task identifier.</summary>
        /// <param name="taskId">The task identifier.</param>
        /// <param name="output">The task output.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("task_{0}")]
        public partial Builder AddTaskOutput(string taskId, TaskOutput output);

        /// <summary>Adds an error output keyed by name.</summary>
        /// <param name="key">The error key.</param>
        /// <param name="error">The error message.</param>
        /// <returns>This builder for chaining.</returns>
        [DictionaryEntry("error_{0}")]
        public partial Builder AddError(string key, string error);
    }
}

/// <summary>
/// Represents a single process output value.
/// </summary>
public sealed class ProcessOutputValue
{
    private readonly object _value;
    private readonly Type _type;

    private ProcessOutputValue(object value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
    }

    /// <summary>Gets the stored value as the specified type.</summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <returns>The value cast to type <typeparamref name="T"/>.</returns>
    public T GetValue<T>() where T : class
    {
        if (_value is T typedValue)
            return typedValue;

        throw new InvalidCastException(
            $"Cannot convert output value of type {_type.Name} to {typeof(T).Name}");
    }

    /// <summary>Gets the raw object value.</summary>
    public object RawValue => _value;
    /// <summary>Gets the runtime type of the stored value.</summary>
    public Type ValueType => _type;

    /// <summary>Creates a new <see cref="ProcessOutputValue"/> wrapping the given object.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A new <see cref="ProcessOutputValue"/>.</returns>
    public static ProcessOutputValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new ProcessOutputValue(value, value.GetType());
    }
}

/// <summary>
/// Strongly typed task outputs container for crew results.
/// </summary>
public sealed class TaskOutputMap
{
    private readonly ImmutableDictionary<string, TaskOutput> _outputs;

    private TaskOutputMap(ImmutableDictionary<string, TaskOutput> outputs)
    {
        _outputs = outputs ?? [];
    }

    /// <summary>
    /// Creates empty task outputs.
    /// </summary>
    private static readonly TaskOutputMap _empty = new([]);
    /// <summary>Gets an empty <see cref="TaskOutputMap"/> instance.</summary>
    public static TaskOutputMap Empty => _empty;

    /// <summary>
    /// Gets a task output by task ID.
    /// </summary>
    public TaskOutput? Get(string taskId)
    {
        return _outputs.TryGetValue(taskId, out var output) ? output : null;
    }

    /// <summary>
    /// Gets all task IDs.
    /// </summary>
    public IEnumerable<string> TaskIds => _outputs.Keys;

    /// <summary>
    /// Gets the count of outputs.
    /// </summary>
    public int Count => _outputs.Count;

    /// <summary>
    /// Adds a task output.
    /// </summary>
    public TaskOutputMap With(string taskId, TaskOutput output)
    {
        return new TaskOutputMap(_outputs.SetItem(taskId, output));
    }

    /// <summary>
    /// Creates from a list of task outputs.
    /// </summary>
    public static TaskOutputMap FromOutputs(IEnumerable<TaskOutput> outputs)
    {
        var dict = outputs.ToImmutableDictionary(o => o.TaskId?.ToString() ?? Guid.NewGuid().ToString());
        return new TaskOutputMap(dict);
    }
}
