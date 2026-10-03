using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Application.Common;

/// <summary>
/// Strongly typed template instantiation parameters.
/// </summary>
[TypedDictionary(typeof(TemplateParameterValue))]
public sealed partial class TemplateInstantiationParameters
{
    /// <summary>
    /// Gets a parameter value.
    /// </summary>
    public T? Get<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>
    /// Gets a required parameter value.
    /// </summary>
    public T GetRequired<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            throw new ArgumentException($"Required template parameter '{key}' not found", nameof(key));

        return value.GetValue<T>();
    }

    /// <summary>
    /// Checks if a parameter exists.
    /// </summary>
    public bool Contains(string key) => _items.ContainsKey(key);

    /// <summary>
    /// Gets all parameter keys.
    /// </summary>
    public IEnumerable<string> Keys => _items.Keys;

    /// <summary>
    /// Gets the count of parameters.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Creates from dictionary (for migration).
    /// </summary>
    public static TemplateInstantiationParameters FromDictionary(IDictionary<string, object> dictionary)
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

    /// <summary>
    /// Converts to dictionary for serialization.
    /// </summary>
    public Dictionary<string, object> ToDictionary()
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in _items)
        {
            result[kvp.Key] = kvp.Value.RawValue;
        }
        return result;
    }

    /// <summary>Builder for constructing <see cref="TemplateInstantiationParameters"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Name.
        /// </summary>
        [DictionaryEntry("name")]
        public partial Builder AddName(string name);

        /// <summary>
        /// Add Role.
        /// </summary>
        [DictionaryEntry("role")]
        public partial Builder AddRole(string role);

        /// <summary>
        /// Add Goal.
        /// </summary>
        [DictionaryEntry("goal")]
        public partial Builder AddGoal(string goal);

        /// <summary>
        /// Add Backstory.
        /// </summary>
        [DictionaryEntry("backstory")]
        public partial Builder AddBackstory(string backstory);

        /// <summary>
        /// Add Description.
        /// </summary>
        [DictionaryEntry("description")]
        public partial Builder AddDescription(string description);

        /// <summary>
        /// Add Expected Output.
        /// </summary>
        [DictionaryEntry("expectedOutput")]
        public partial Builder AddExpectedOutput(string output);

        /// <summary>
        /// Add Tools.
        /// </summary>
        [DictionaryEntry("tools")]
        public partial Builder AddTools(IReadOnlyList<string> tools);

        /// <summary>
        /// Add Max Iterations.
        /// </summary>
        [DictionaryEntry("maxIterations")]
        public partial Builder AddMaxIterations(int iterations);

        /// <summary>
        /// Add Timeout.
        /// </summary>
        [DictionaryEntry("timeout")]
        public partial Builder AddTimeout(TimeSpan timeout);
    }
}

/// <summary>
/// Template parameter value wrapper.
/// </summary>
public sealed class TemplateParameterValue
{
    private readonly object _value;
    private readonly Type _type;

    private TemplateParameterValue(object value, Type type)
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
                $"Cannot convert template parameter value of type {_type.Name} to {typeof(T).Name}", ex);
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
    public static TemplateParameterValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new TemplateParameterValue(value, value.GetType());
    }
}
