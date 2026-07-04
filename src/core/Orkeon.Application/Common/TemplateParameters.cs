using System.Collections.Immutable;
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
/// Invalid template parameters collection.
/// </summary>
[TypedDictionary(typeof(string), EmitGenericAdd = false)]
public sealed partial class InvalidTemplateParameters
{
    /// <summary>
    /// Gets an error message for a parameter.
    /// </summary>
    public string? GetError(string parameter)
    {
        return _items.TryGetValue(parameter, out var error) ? error : null;
    }

    /// <summary>
    /// Gets all parameter names with errors.
    /// </summary>
    public IEnumerable<string> Parameters => _items.Keys;

    /// <summary>
    /// Gets all error messages.
    /// </summary>
    public IEnumerable<string> Errors => _items.Values;

    /// <summary>
    /// Gets the count of invalid parameters.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Checks if a parameter has an error.
    /// </summary>
    public bool HasError(string parameter) => _items.ContainsKey(parameter);

    /// <summary>
    /// Creates from dictionary.
    /// </summary>
    public static InvalidTemplateParameters FromDictionary(Dictionary<string, string> errors)
    {
        return new InvalidTemplateParameters(errors.ToImmutableDictionary());
    }

    /// <summary>
    /// Converts to dictionary.
    /// </summary>
    public Dictionary<string, string> ToDictionary()
    {
        return _items.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>Builder for constructing <see cref="InvalidTemplateParameters"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Error.
        /// </summary>
        [DictionaryEntry(Factory = "")]
        public partial Builder AddError(string parameter, string error);

        /// <summary>
        /// Add Type Error.
        /// </summary>
        public Builder AddTypeError(string parameter, Type expectedType, Type actualType)
        {
            ArgumentNullException.ThrowIfNull(expectedType);
            ArgumentNullException.ThrowIfNull(actualType);
            _items[parameter] = $"Expected type {expectedType.Name} but got {actualType.Name}";
            return this;
        }

        /// <summary>
        /// Add Missing Error.
        /// </summary>
        public Builder AddMissingError(string parameter)
        {
            _items[parameter] = $"Required parameter '{parameter}' is missing";
            return this;
        }

        /// <summary>
        /// Add Range Error.
        /// </summary>
        public Builder AddRangeError(string parameter, object min, object max, object actual)
        {
            _items[parameter] = $"Value {actual} is outside the allowed range [{min}, {max}]";
            return this;
        }
    }
}

/// <summary>
/// Resolved template parameters after processing.
/// </summary>
[TypedDictionary(typeof(TemplateParameterValue))]
public sealed partial class ResolvedTemplateParameters
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
    /// Creates resolved parameters from instantiation parameters.
    /// </summary>
    public static ResolvedTemplateParameters FromInstantiationParameters(
        TemplateInstantiationParameters original,
        Dictionary<string, object>? additionalResolved = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        var builder = CreateBuilder();

        // Copy original parameters
        foreach (var key in original.Keys)
        {
            var value = original.Get<object>(key);
            if (value != null)
                builder.Add(key, value);
        }

        // Add additional resolved values
        if (additionalResolved != null)
        {
            foreach (var kvp in additionalResolved)
            {
                builder.Add(kvp.Key, kvp.Value);
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Converts to dictionary.
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
}

/// <summary>
/// Template configuration parameters.
/// </summary>
[TypedDictionary(typeof(TemplateParameterValue))]
public sealed partial class TemplateConfiguration
{
    /// <summary>
    /// Gets a configuration value.
    /// </summary>
    public T? Get<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>
    /// Converts to dictionary.
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

    /// <summary>
    /// Creates from dictionary.
    /// </summary>
    public static TemplateConfiguration FromDictionary(Dictionary<string, object> dictionary)
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

    /// <summary>Builder for constructing <see cref="TemplateConfiguration"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Template.
        /// </summary>
        [DictionaryEntry("templateId")]
        public partial Builder AddTemplate(string templateId);

        /// <summary>
        /// Add Version.
        /// </summary>
        [DictionaryEntry("version")]
        public partial Builder AddVersion(string version);

        /// <summary>
        /// Add Environment.
        /// </summary>
        [DictionaryEntry("environment")]
        public partial Builder AddEnvironment(string environment);
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
