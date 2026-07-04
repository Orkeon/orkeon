using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Application.Execution;

/// <summary>
/// Strongly typed execution variables for direct execution context.
/// </summary>
[TypedDictionary(typeof(ExecutionVariableValue), CacheEmpty = true, EmitBuilderFrom = true)]
public sealed partial class DirectExecutionVariables
{
    /// <summary>
    /// Get.
    /// </summary>
    public T? Get<T>(string key) where T : class
    {
        if (!_items.TryGetValue(key, out var value))
            return null;

        return value.GetValue<T>();
    }

    /// <summary>
    /// Get String.
    /// </summary>
    public string GetString(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return string.Empty;

        var obj = value.GetValue<object>();

        return obj switch
        {
            double d => d.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            null => string.Empty,
            _ => obj.ToString() ?? string.Empty
        };
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
    /// Set.
    /// </summary>
    public DirectExecutionVariables Set(string key, object value)
    {
        var newVariables = _items.SetItem(key, ExecutionVariableValue.From(value));
        return new DirectExecutionVariables(newVariables);
    }

    /// <summary>
    /// Set Multiple.
    /// </summary>
    public DirectExecutionVariables SetMultiple(IEnumerable<KeyValuePair<string, object>> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var builder = _items.ToBuilder();
        foreach (var item in items)
        {
            builder[item.Key] = ExecutionVariableValue.From(item.Value);
        }
        return new DirectExecutionVariables(builder.ToImmutable());
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

    /// <summary>
    /// From Dictionary.
    /// </summary>
    public static DirectExecutionVariables FromDictionary(IReadOnlyDictionary<string, object>? dictionary)
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

    /// <summary>Builder for constructing <see cref="DirectExecutionVariables"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Task Id.
        /// </summary>
        [DictionaryEntry("task_id")]
        public partial Builder AddTaskId(string taskId);

        /// <summary>
        /// Add Agent Id.
        /// </summary>
        [DictionaryEntry("agent_id")]
        public partial Builder AddAgentId(string agentId);

        /// <summary>
        /// Add Input.
        /// </summary>
        [DictionaryEntry("input")]
        public partial Builder AddInput(string input);

        /// <summary>
        /// Add Context.
        /// </summary>
        [DictionaryEntry("context")]
        public partial Builder AddContext(Dictionary<string, string> context);
    }
}

/// <summary>
/// Strongly typed task results collection.
/// </summary>
public sealed class TaskResultsMap
{
    private readonly ImmutableDictionary<string, TaskResultValue> _results;

    private TaskResultsMap(ImmutableDictionary<string, TaskResultValue> results)
    {
        _results = results ?? [];
    }

    /// <summary>Gets an empty <see cref="TaskResultsMap"/> instance.</summary>
    public static TaskResultsMap Empty => new([]);

    /// <summary>
    /// Get.
    /// </summary>
    public T? Get<T>(string taskId) where T : class
    {
        if (!_results.TryGetValue(taskId, out var value))
            return null;

        return value.GetValue<T>();
    }

    /// <summary>
    /// Get As String.
    /// </summary>
    public string GetAsString(string taskId)
    {
        if (!_results.TryGetValue(taskId, out var value))
            return string.Empty;

        var obj = value.GetValue<object>();
        return obj?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Contains Task.
    /// </summary>
    public bool ContainsTask(string taskId) => _results.ContainsKey(taskId);

    /// <summary>
    /// Gets or sets the task ids.
    /// </summary>
    public IEnumerable<string> TaskIds => _results.Keys;

    /// <summary>
    /// Add.
    /// </summary>
    public TaskResultsMap Add(string taskId, object result)
    {
        var newResults = _results.SetItem(taskId, TaskResultValue.From(result));
        return new TaskResultsMap(newResults);
    }

    /// <summary>
    /// To Dictionary.
    /// </summary>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in _results)
        {
            result[kvp.Key] = kvp.Value.RawValue;
        }
        return result;
    }
}

/// <summary>
/// Value wrapper for execution variables.
/// </summary>
public sealed class ExecutionVariableValue
{
    private readonly object _value;
    private readonly Type _type;

    private ExecutionVariableValue(object value, Type type)
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
                $"Cannot convert execution variable value of type {_type.Name} to {typeof(T).Name}", ex);
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
    public static ExecutionVariableValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new ExecutionVariableValue(value, value.GetType());
    }
}

/// <summary>
/// Value wrapper for task results.
/// </summary>
public sealed class TaskResultValue
{
    private readonly object _value;
    private readonly Type _type;
    private readonly DateTime _timestamp;

    private TaskResultValue(object value, Type type)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        ArgumentNullException.ThrowIfNull(type);
        _type = type;
        _timestamp = DateTime.UtcNow;
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
                $"Cannot convert task result value of type {_type.Name} to {typeof(T).Name}", ex);
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
    /// Timestamp.
    /// </summary>
    public DateTime Timestamp => _timestamp;

    /// <summary>
    /// From.
    /// </summary>
    public static TaskResultValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new TaskResultValue(value, value.GetType());
    }
}
