using Orkeon.Domain.Tools.Protocol;
using Orkeon.Trading.Tools.Domain.Serialization;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orkeon.Trading.Tools.Infrastructure.Base;

/// <summary>
/// Generic typed base class for trading tools. Eliminates parameter parsing
/// and result-building boilerplate by automating the pipeline:
/// <c>Dictionary → TRequest → ExecuteTypedAsync → TResponse → Dictionary</c>.
/// </summary>
/// <typeparam name="TRequest">
///   Strongly-typed request record. Property names are mapped to/from snake_case
///   YAML parameter names via <see cref="JsonNamingPolicy.SnakeCaseLower"/>.
/// </typeparam>
/// <typeparam name="TResponse">
///   Strongly-typed response record. Property names are serialized to snake_case
///   keys matching the YAML returns schema for FilterOutput compatibility.
/// </typeparam>
public abstract class TradingToolBase<TRequest, TResponse> : TradingToolBase
    where TRequest : class, new()
    where TResponse : class
{
    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new BoolTolerantConverter(),
            new IntTolerantConverter(),
            new LongTolerantConverter(),
            new DecimalInvariantConverter(),
            new DoubleInvariantConverter(),
            new RawObjectConverter()
        }
    };

    /// <summary>
    /// Tolerant boolean converter: accepts JSON booleans, strings ("true"/"false"),
    /// and numeric values (0/non-zero). Handles YAML defaults that arrive as strings.
    /// </summary>
    private class BoolTolerantConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.String when bool.TryParse(reader.GetString(), out var b) => b,
                JsonTokenType.Number => reader.TryGetInt64(out var n) ? n != 0 : reader.GetDouble() != 0,
                _ => throw new JsonException($"Cannot convert token type '{reader.TokenType}' to boolean")
            };
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
            => writer.WriteBooleanValue(value);
    }

    /// <summary>
    /// Tolerant int converter: accepts JSON numbers and numeric strings ("5").
    /// Handles YAML defaults that arrive as strings.
    /// </summary>
    private class IntTolerantConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetInt32(),
                JsonTokenType.String when int.TryParse(reader.GetString(), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var i) => i,
                _ => throw new JsonException($"Cannot convert token type '{reader.TokenType}' to int")
            };
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }

    /// <summary>
    /// Tolerant long converter: accepts JSON numbers and numeric strings.
    /// Handles YAML defaults that arrive as strings.
    /// </summary>
    private class LongTolerantConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetInt64(),
                JsonTokenType.String when long.TryParse(reader.GetString(), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var l) => l,
                _ => throw new JsonException($"Cannot convert token type '{reader.TokenType}' to long")
            };
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value);
    }

    /// <summary>
    /// Converts JSON values to raw .NET types when the target type is <see cref="object"/>.
    /// Without this, <see cref="JsonSerializer"/> produces <see cref="JsonElement"/> values
    /// inside <c>Dictionary&lt;string, object&gt;</c>, breaking downstream casts.
    /// </summary>
    private class RawObjectConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number when reader.TryGetInt32(out var i) => i,
                JsonTokenType.Number when reader.TryGetInt64(out var l) => l,
                JsonTokenType.Number when reader.TryGetDecimal(out var d) => d,
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Null => null,
                JsonTokenType.StartArray => ReadArray(ref reader, options),
                JsonTokenType.StartObject => ReadObject(ref reader, options),
                _ => throw new JsonException($"Unexpected token: {reader.TokenType}")
            };
        }

        private static List<object> ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            var list = new List<object>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                var converter = (RawObjectConverter)options.GetConverter(typeof(object));
                list.Add(converter.Read(ref reader, typeof(object), options)!);
            }
            return list;
        }

        private static Dictionary<string, object> ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
        {
            var dict = new Dictionary<string, object>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var key = reader.GetString()!;
                reader.Read();
                var converter = (RawObjectConverter)options.GetConverter(typeof(object));
                dict[key] = converter.Read(ref reader, typeof(object), options)!;
            }
            return dict;
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value is null) { writer.WriteNullValue(); return; }
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }

    protected TradingToolBase(ILogger? logger = null) : base(logger) { }

    /// <summary>
    /// Sealed override: handles deserialization, default injection, typed dispatch,
    /// and response conversion. Tools override <see cref="ExecuteTypedAsync"/> instead.
    /// </summary>
    protected sealed override async Task<ToolCallResponse> ExecuteCoreAsync(
        ToolCallRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Step 1: Inject YAML defaults for missing optional parameters
            var mergedParams = MergeWithYamlDefaults(request.Parameters);

            // Step 2: Deserialize merged parameters → TRequest
            var typedRequest = DeserializeRequest(mergedParams);

            // Step 3: Optional custom validation hook
            var validationError = ValidateTypedRequest(typedRequest);
            if (validationError is not null)
            {
                return new ToolCallResponse(Success: false, Result: null, Error: validationError);
            }

            // Step 4: Dispatch to typed business logic
            var typedResponse = await ExecuteTypedAsync(typedRequest, cancellationToken);

            // Step 5: Convert TResponse → Dictionary<string, object> for FilterOutput
            var resultDict = SerializeResponse(typedResponse);

            return new ToolCallResponse(Success: true, Result: resultDict, Error: null);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Parameter deserialization failed for tool {ToolName}", Name);
            return new ToolCallResponse(
                Success: false,
                Result: null,
                Error: $"Invalid parameters: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error executing tool {ToolName}", Name);
            return new ToolCallResponse(
                Success: false,
                Result: null,
                Error: $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Pure business logic. Implement this instead of ExecuteCoreAsync.
    /// Parameter parsing and result conversion are handled by the base class.
    /// </summary>
    protected abstract Task<TResponse> ExecuteTypedAsync(
        TRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Optional: override to add domain-level validation beyond schema-level required checks.
    /// Return null if valid, or an error message string if invalid.
    /// </summary>
    protected virtual string? ValidateTypedRequest(TRequest request) => null;

    /// <summary>
    /// Merges YAML default values into the parameters dictionary for any optional
    /// parameters that are missing from the request. Converts YAML defaults
    /// to their proper typed values using the declared parameter type.
    /// </summary>
    private Dictionary<string, object?> MergeWithYamlDefaults(Dictionary<string, object?> parameters)
    {
        var merged = new Dictionary<string, object?>(parameters, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, paramDef) in Schema.Parameters)
        {
            if (!paramDef.Required && paramDef.Default is not null && !merged.ContainsKey(key))
            {
                merged[key] = CoerceYamlDefault(paramDef.Default, paramDef.Type);
            }
        }

        return merged;
    }

    /// <summary>
    /// Converts a YAML default value (which may be a string due to YamlDotNet's
    /// object deserialization) to the proper .NET type based on the declared YAML type.
    /// </summary>
    private static object CoerceYamlDefault(object value, string yamlType)
    {
        var str = value.ToString();
        if (str is null) return value;

        return yamlType switch
        {
            "boolean" when bool.TryParse(str, out var b) => b,
            "integer" when int.TryParse(str, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var i) => i,
            "number" when double.TryParse(str, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
            _ => value
        };
    }

    /// <summary>
    /// Deserializes the parameters dictionary to TRequest via JSON round-trip
    /// using snake_case naming policy.
    /// First normalizes any JsonElement values to raw .NET types and converts
    /// camelCase/PascalCase keys to snake_case, ensuring consistency with the
    /// <see cref="JsonNamingPolicy.SnakeCaseLower"/> naming policy for nested objects.
    /// </summary>
    private static TRequest DeserializeRequest(Dictionary<string, object?> parameters)
    {
        var normalized = NormalizeParameters(parameters);
        var json = JsonSerializer.Serialize(normalized, SnakeCaseOptions);
        return JsonSerializer.Deserialize<TRequest>(json, SnakeCaseOptions)
               ?? throw new JsonException($"Failed to deserialize parameters to {typeof(TRequest).Name}");
    }

    /// <summary>
    /// Recursively converts JsonElement values in the parameters dictionary to raw .NET types
    /// (string, int, decimal, bool, List, Dictionary) and normalizes camelCase/PascalCase keys
    /// to snake_case so they match the <see cref="JsonNamingPolicy.SnakeCaseLower"/> naming policy.
    /// Data keys (all-uppercase like stock symbols, or already snake_case) are preserved as-is.
    /// </summary>
    private static Dictionary<string, object?> NormalizeParameters(Dictionary<string, object?> parameters)
    {
        var normalized = new Dictionary<string, object?>(parameters.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parameters)
        {
            normalized[key] = NormalizeValue(value);
        }
        return normalized;
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => NormalizeJsonObject(element),
                JsonValueKind.Array => NormalizeJsonArray(element),
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt32(out var i) => i,
                JsonValueKind.Number when element.TryGetInt64(out var l) => l,
                JsonValueKind.Number when element.TryGetDecimal(out var d) => d,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => value
            };
        }

        // Handle raw Dictionary<string, object> — normalize keys to snake_case recursively.
        // This handles cases where the Crew workflow passes parameters as already-deserialized
        // dictionaries with PascalCase/camelCase keys (e.g., "AssetClass" → "asset_class").
        if (value is IDictionary<string, object> dict)
        {
            return NormalizeRawDictionary(dict);
        }

        // Handle raw lists — recursively normalize items.
        // Uses non-generic IList to catch List<T> for any T (List<PortfolioPosition>,
        // List<Dictionary<string, object>>, etc.). Strings are excluded (IEnumerable<char>).
        if (value is System.Collections.IList list && value is not string)
        {
            return NormalizeRawList(list);
        }

        return value;
    }

    private static Dictionary<string, object> NormalizeJsonObject(JsonElement element)
    {
        var dict = new Dictionary<string, object>();
        foreach (var prop in element.EnumerateObject())
        {
            var key = NormalizeKeyToSnakeCase(prop.Name);
            var val = NormalizeValue(prop.Value);
            if (val is not null)
                dict[key] = val;
        }
        return dict;
    }

    private static List<object> NormalizeJsonArray(JsonElement element)
    {
        var list = new List<object>();
        foreach (var item in element.EnumerateArray())
        {
            var val = NormalizeValue(item);
            if (val is not null)
                list.Add(val);
        }
        return list;
    }

    /// <summary>
    /// Normalizes a raw Dictionary&lt;string, object&gt; by converting camelCase/PascalCase
    /// keys to snake_case and recursively normalizing values.
    /// </summary>
    private static Dictionary<string, object> NormalizeRawDictionary(IDictionary<string, object> dict)
    {
        var normalized = new Dictionary<string, object>(dict.Count);
        foreach (var (key, val) in dict)
        {
            var normalizedKey = NormalizeKeyToSnakeCase(key);
            var normalizedVal = NormalizeValue(val);
            if (normalizedVal is not null)
                normalized[normalizedKey] = normalizedVal;
        }
        return normalized;
    }

    /// <summary>
    /// Normalizes a raw IList by recursively normalizing each item.
    /// </summary>
    private static List<object> NormalizeRawList(System.Collections.IList list)
    {
        var normalized = new List<object>(list.Count);
        foreach (var item in list)
        {
            var val = NormalizeValue(item);
            if (val is not null)
                normalized.Add(val);
        }
        return normalized;
    }

    /// <summary>
    /// Converts camelCase and PascalCase keys to snake_case using the SnakeCaseLower naming policy.
    /// Data keys (all-uppercase like stock symbols "AAPL", all-lowercase "symbol",
    /// or already snake_case "portfolio_value") are preserved as-is.
    /// </summary>
    private static string NormalizeKeyToSnakeCase(string key)
    {
        if (key.Length <= 1) return key;

        // camelCase: starts lowercase, contains at least one uppercase letter
        // e.g., "assetClass" → "asset_class", "currentPrice" → "current_price"
        bool isCamelCase = char.IsLower(key[0]) && key.Any(char.IsUpper);

        // PascalCase: starts uppercase, contains at least one lowercase letter, no underscores
        // e.g., "AssetClass" → "asset_class", "Symbol" → "symbol"
        // Excludes: "AAPL" (all uppercase → stock symbol), "snake_case" (has underscore)
        bool isPascalCase = char.IsUpper(key[0]) && key.Any(char.IsLower) && !key.Contains('_');

        if (isCamelCase || isPascalCase)
        {
            return JsonNamingPolicy.SnakeCaseLower.ConvertName(key);
        }

        return key;
    }

    /// <summary>
    /// Serializes TResponse to Dictionary&lt;string, object&gt; via JSON round-trip
    /// so the existing FilterOutput pipeline works unchanged.
    /// </summary>
    private static Dictionary<string, object> SerializeResponse(TResponse response)
    {
        var json = JsonSerializer.Serialize(response, SnakeCaseOptions);
        return JsonSerializer.Deserialize<Dictionary<string, object>>(json, SnakeCaseOptions)
               ?? new Dictionary<string, object>();
    }
}
