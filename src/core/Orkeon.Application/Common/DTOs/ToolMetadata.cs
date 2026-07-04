using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Strongly typed tool metadata.
/// </summary>
[TypedDictionary(typeof(ToolMetadataValue))]
public sealed partial class ToolDtoMetadata
{
    /// <summary>
    /// Gets a metadata value.
    /// </summary>
    public T? Get<T>(string key)
    {
        if (!_items.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
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

    /// <summary>Builder for constructing <see cref="ToolDtoMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Tool Type.
        /// </summary>
        [DictionaryEntry("tool_type")]
        public partial Builder AddToolType(string toolType);

        /// <summary>
        /// Add Tool Name.
        /// </summary>
        [DictionaryEntry("tool_name")]
        public partial Builder AddToolName(string toolName);

        /// <summary>
        /// Add Category.
        /// </summary>
        [DictionaryEntry("category")]
        public partial Builder AddCategory(string category);
    }
}

/// <summary>
/// Tool configuration data.
/// </summary>
[TypedDictionary(typeof(ToolMetadataValue))]
public sealed partial class ToolConfigurationData
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
    /// Creates default tool configuration.
    /// </summary>
    public static ToolConfigurationData CreateDefault()
    {
        return CreateBuilder()
            .AddTimeoutSeconds(30)
            .AddRetryAttempts(3)
            .AddCacheResults(true)
            .AddValidateInput(true)
            .AddSanitizeOutput(true)
            .Build();
    }

    /// <summary>Builder for constructing <see cref="ToolConfigurationData"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Timeout Seconds.
        /// </summary>
        [DictionaryEntry("timeout_seconds")]
        public partial Builder AddTimeoutSeconds(int seconds);

        /// <summary>
        /// Add Retry Attempts.
        /// </summary>
        [DictionaryEntry("retry_attempts")]
        public partial Builder AddRetryAttempts(int attempts);

        /// <summary>
        /// Add Cache Results.
        /// </summary>
        [DictionaryEntry("cache_results")]
        public partial Builder AddCacheResults(bool cache);

        /// <summary>
        /// Add Validate Input.
        /// </summary>
        [DictionaryEntry("validate_input")]
        public partial Builder AddValidateInput(bool validate);

        /// <summary>
        /// Add Sanitize Output.
        /// </summary>
        [DictionaryEntry("sanitize_output")]
        public partial Builder AddSanitizeOutput(bool sanitize);
    }
}

/// <summary>
/// Tool validation rules.
/// </summary>
[TypedDictionary(typeof(ToolMetadataValue))]
public sealed partial class ToolValidationRules
{
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

    /// <summary>Builder for constructing <see cref="ToolValidationRules"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Required Fields.
        /// </summary>
        [DictionaryEntry("required_fields")]
        public partial Builder AddRequiredFields(IReadOnlyList<string> fields);

        /// <summary>
        /// Add Max Length.
        /// </summary>
        [DictionaryEntry("{0}_max_length")]
        public partial Builder AddMaxLength(string field, int maxLength);

        /// <summary>
        /// Add Pattern.
        /// </summary>
        [DictionaryEntry("{0}_pattern")]
        public partial Builder AddPattern(string field, string pattern);
    }
}

/// <summary>
/// Tool input data from metadata.
/// </summary>
[TypedDictionary(typeof(ToolMetadataValue), EmitGenericAdd = false)]
public sealed partial class ToolInputData
{
    /// <summary>
    /// Creates default parsed input.
    /// </summary>
    public static ToolInputData CreateParsedFromMetadata()
    {
        return CreateBuilder()
            .AddParsedFromMetadata(true)
            .Build();
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

    /// <summary>Builder for constructing <see cref="ToolInputData"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Parsed From Metadata.
        /// </summary>
        [DictionaryEntry("parsed_from_metadata")]
        public partial Builder AddParsedFromMetadata(bool parsed);

        /// <summary>
        /// Add Input.
        /// </summary>
        [DictionaryEntry]
        public partial Builder AddInput(string key, object value);

        /// <summary>
        /// Add Query.
        /// </summary>
        [DictionaryEntry("query")]
        public partial Builder AddQuery(string query);

        /// <summary>
        /// Add File Path.
        /// </summary>
        [DictionaryEntry("file_path")]
        public partial Builder AddFilePath(string path);

        /// <summary>
        /// Add Url.
        /// </summary>
        [DictionaryEntry("url")]
        public partial Builder AddUrl(Uri url);
    }
}

/// <summary>
/// Tool metadata value wrapper.
/// </summary>
public sealed class ToolMetadataValue
{
    private readonly object _value;
    private readonly Type _type;

    private ToolMetadataValue(object value, Type type)
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
                $"Cannot convert tool metadata value of type {_type.Name} to {typeof(T).Name}", ex);
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
    public static ToolMetadataValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new ToolMetadataValue(value, value.GetType());
    }
}
