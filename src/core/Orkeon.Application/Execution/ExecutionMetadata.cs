using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Application.Execution;

/// <summary>
/// Strongly typed metadata for crew execution state.
/// </summary>
[TypedDictionary(typeof(ExecutionMetadataValue), CacheEmpty = true, EmitBuilderFrom = true)]
public sealed partial class ExecutionMetadata
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
    /// Get Required.
    /// </summary>
    public T GetRequired<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            throw new KeyNotFoundException($"Required metadata key '{key}' not found");

        return value.GetValue<T>();
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
    public static ExecutionMetadata FromDictionary(IDictionary<string, object>? dictionary)
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

    /// <summary>Builder for constructing <see cref="ExecutionMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Iteration Count.
        /// </summary>
        [DictionaryEntry("iteration_count")]
        public partial Builder AddIterationCount(int count);

        /// <summary>
        /// Add Retry Count.
        /// </summary>
        [DictionaryEntry("retry_count")]
        public partial Builder AddRetryCount(int count);

        /// <summary>
        /// Add Execution Mode.
        /// </summary>
        [DictionaryEntry("execution_mode")]
        public partial Builder AddExecutionMode(string mode);

        /// <summary>
        /// Add Parent Execution Id.
        /// </summary>
        [DictionaryEntry("parent_execution_id")]
        public partial Builder AddParentExecutionId(string parentId);

        /// <summary>
        /// Add User Id.
        /// </summary>
        [DictionaryEntry("user_id")]
        public partial Builder AddUserId(string userId);

        /// <summary>
        /// Add Session Id.
        /// </summary>
        [DictionaryEntry("session_id")]
        public partial Builder AddSessionId(string sessionId);

        /// <summary>
        /// Add Priority.
        /// </summary>
        [DictionaryEntry("priority")]
        public partial Builder AddPriority(int priority);

        /// <summary>
        /// Add Tags.
        /// </summary>
        [DictionaryEntry("tags")]
        public partial Builder AddTags(string[] tags);

        /// <summary>
        /// Add Resource Constraints.
        /// </summary>
        [DictionaryEntry("resource_constraints")]
        public partial Builder AddResourceConstraints(Dictionary<string, double> constraints);

        /// <summary>
        /// Add Checkpoint.
        /// </summary>
        [DictionaryEntry("checkpoint")]
        public partial Builder AddCheckpoint(string checkpointData);
    }
}

/// <summary>
/// Value wrapper for execution metadata.
/// </summary>
public sealed class ExecutionMetadataValue
{
    private readonly object _value;
    private readonly Type _type;

    private ExecutionMetadataValue(object value, Type type)
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
                $"Cannot convert execution metadata value of type {_type.Name} to {typeof(T).Name}", ex);
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
    public static ExecutionMetadataValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new ExecutionMetadataValue(value, value.GetType());
    }
}
