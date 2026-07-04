using System.Collections.Immutable;
using System.Globalization;
using Orkeon.Generators;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Base class for API metadata types.
/// </summary>
public abstract class ApiMetadataBase
{
    /// <summary>Gets the backing metadata entries.</summary>
    protected ImmutableDictionary<string, ApiMetadataValue> Data { get; }

    /// <summary>Initializes a new instance of <see cref="ApiMetadataBase"/>.</summary>
    protected ApiMetadataBase(ImmutableDictionary<string, ApiMetadataValue> data)
    {
        Data = data ?? [];
    }

    /// <summary>
    /// Get.
    /// </summary>
    public T? Get<T>(string key)
    {
        if (!Data.TryGetValue(key, out var value))
            return default;

        return value.GetValue<T>();
    }

    /// <summary>
    /// Get Required.
    /// </summary>
    public T GetRequired<T>(string key)
    {
        if (!Data.TryGetValue(key, out var value))
            throw new ArgumentException($"Required metadata '{key}' not found", nameof(key));

        return value.GetValue<T>();
    }

    /// <summary>
    /// Contains.
    /// </summary>
    public bool Contains(string key) => Data.ContainsKey(key);
    /// <summary>
    /// Gets the keys.
    /// </summary>
    public IEnumerable<string> Keys => Data.Keys;
    /// <summary>
    /// Gets the number of entries.
    /// </summary>
    public int Count => Data.Count;

    /// <summary>
    /// To Immutable Dictionary.
    /// </summary>
    public ImmutableDictionary<string, object> ToImmutableDictionary()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, object>();
        foreach (var kvp in Data)
        {
            builder.Add(kvp.Key, kvp.Value.RawValue);
        }
        return builder.ToImmutable();
    }
}

/// <summary>
/// API response metadata.
/// </summary>
[TypedDictionary(typeof(ApiMetadataValue))]
public sealed partial class ApiResponseMetadata : ApiMetadataBase
{
    /// <summary>Builder for constructing <see cref="ApiResponseMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Request Duration.
        /// </summary>
        [DictionaryEntry("requestDuration")]
        public partial Builder AddRequestDuration(TimeSpan duration);

        /// <summary>
        /// Add Server Version.
        /// </summary>
        [DictionaryEntry("serverVersion")]
        public partial Builder AddServerVersion(string version);

        /// <summary>
        /// Add Trace Id.
        /// </summary>
        [DictionaryEntry("traceId")]
        public partial Builder AddTraceId(string traceId);
    }
}

/// <summary>
/// Error details metadata.
/// </summary>
[TypedDictionary(typeof(ApiMetadataValue))]
public sealed partial class ErrorDetails : ApiMetadataBase
{
    /// <summary>Builder for constructing <see cref="ErrorDetails"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Stack Trace.
        /// </summary>
        [DictionaryEntry("stackTrace")]
        public partial Builder AddStackTrace(string stackTrace);

        /// <summary>
        /// Add Inner Error.
        /// </summary>
        [DictionaryEntry("innerError")]
        public partial Builder AddInnerError(string innerError);

        /// <summary>
        /// Add Error Source.
        /// </summary>
        [DictionaryEntry("errorSource")]
        public partial Builder AddErrorSource(string source);
    }
}

/// <summary>
/// Crew/Agent/Task metadata for API DTOs.
/// </summary>
[TypedDictionary(typeof(ApiMetadataValue), EmitGenericAdd = false)]
public sealed partial class EntityMetadata : ApiMetadataBase
{
    /// <summary>Builder for constructing <see cref="EntityMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Created By.
        /// </summary>
        [DictionaryEntry("createdBy")]
        public partial Builder AddCreatedBy(string userId);

        /// <summary>
        /// Add Tags.
        /// </summary>
        [DictionaryEntry("tags")]
        public partial Builder AddTags(IReadOnlyList<string> tags);

        /// <summary>
        /// Add Version.
        /// </summary>
        [DictionaryEntry("version")]
        public partial Builder AddVersion(int version);

        /// <summary>
        /// Add Custom Field.
        /// </summary>
        [DictionaryEntry]
        public partial Builder AddCustomField(string key, object value);
    }
}

/// <summary>
/// Task context metadata.
/// </summary>
[TypedDictionary(typeof(ApiMetadataValue))]
public sealed partial class TaskContextMetadata : ApiMetadataBase
{
    /// <summary>Builder for constructing <see cref="TaskContextMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Input Data.
        /// </summary>
        [DictionaryEntry("inputData")]
        public partial Builder AddInputData(string data);

        /// <summary>
        /// Add Expected Format.
        /// </summary>
        [DictionaryEntry("expectedFormat")]
        public partial Builder AddExpectedFormat(string format);

        /// <summary>
        /// Add Validation Rules.
        /// </summary>
        [DictionaryEntry("validationRules")]
        public partial Builder AddValidationRules(IReadOnlyList<string> rules);
    }
}

/// <summary>
/// Execution parameters metadata.
/// </summary>
[TypedDictionary(typeof(ApiMetadataValue))]
public sealed partial class ExecutionParameters : ApiMetadataBase
{
    /// <summary>Builder for constructing <see cref="ExecutionParameters"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Max Retries.
        /// </summary>
        [DictionaryEntry("maxRetries")]
        public partial Builder AddMaxRetries(int retries);

        /// <summary>
        /// Add Timeout.
        /// </summary>
        [DictionaryEntry("timeout")]
        public partial Builder AddTimeout(TimeSpan timeout);

        /// <summary>
        /// Add Priority.
        /// </summary>
        [DictionaryEntry("priority")]
        public partial Builder AddPriority(string priority);
    }
}

/// <summary>
/// Execution context metadata.
/// </summary>
[TypedDictionary(typeof(ApiMetadataValue))]
public sealed partial class ExecutionContext : ApiMetadataBase
{
    /// <summary>Builder for constructing <see cref="ExecutionContext"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Environment.
        /// </summary>
        [DictionaryEntry("environment")]
        public partial Builder AddEnvironment(string environment);

        /// <summary>
        /// Add User Id.
        /// </summary>
        [DictionaryEntry("userId")]
        public partial Builder AddUserId(string userId);

        /// <summary>
        /// Add Session Id.
        /// </summary>
        [DictionaryEntry("sessionId")]
        public partial Builder AddSessionId(string sessionId);
    }
}

/// <summary>
/// Search filters metadata.
/// </summary>
[TypedDictionary(typeof(ApiMetadataValue))]
public sealed partial class SearchFilters : ApiMetadataBase
{
    /// <summary>Builder for constructing <see cref="SearchFilters"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Date Range.
        /// </summary>
        public Builder AddDateRange(DateTime start, DateTime end)
        {
            _items["dateStart"] = ApiMetadataValue.From(start);
            _items["dateEnd"] = ApiMetadataValue.From(end);
            return this;
        }

        /// <summary>
        /// Add Status.
        /// </summary>
        [DictionaryEntry("status")]
        public partial Builder AddStatus(string status);

        /// <summary>
        /// Add Tags.
        /// </summary>
        [DictionaryEntry("tags")]
        public partial Builder AddTags(IReadOnlyList<string> tags);
    }
}

/// <summary>
/// Component health metadata.
/// </summary>
[TypedDictionary(typeof(ApiMetadataValue))]
public sealed partial class ComponentHealthMetadata : ApiMetadataBase
{
    /// <summary>Builder for constructing <see cref="ComponentHealthMetadata"/> instances.</summary>
    public sealed partial class Builder
    {
        /// <summary>
        /// Add Version.
        /// </summary>
        [DictionaryEntry("version")]
        public partial Builder AddVersion(string version);

        /// <summary>
        /// Add Connection Count.
        /// </summary>
        [DictionaryEntry("connectionCount")]
        public partial Builder AddConnectionCount(int count);

        /// <summary>
        /// Add Last Error.
        /// </summary>
        [DictionaryEntry("lastError")]
        public partial Builder AddLastError(string error);
    }
}

/// <summary>
/// API metadata value wrapper.
/// </summary>
public sealed class ApiMetadataValue
{
    private readonly object _value;
    private readonly Type _type;

    private ApiMetadataValue(object value, Type type)
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
                $"Cannot convert API metadata value of type {_type.Name} to {typeof(T).Name}", ex);
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
    public static ApiMetadataValue From(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new ApiMetadataValue(value, value.GetType());
    }
}
