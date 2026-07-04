using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Analysis.Abstractions;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Serialization.Converters;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Infrastructure.Serialization;

/// <summary>
/// JSON-based implementation of <see cref="IComponentSerializer"/>.
/// Encapsulates System.Text.Json serialization with snake_case naming,
/// tolerant type coercion converters, and JsonElement unwrapping.
/// </summary>
public sealed class JsonComponentSerializer : IComponentSerializer
{
    /// <summary>
    /// Thread-safe singleton instance.
    /// </summary>
    public static readonly JsonComponentSerializer Instance = new();

    /// <summary>
    /// Shared JSON options with snake_case naming and tolerant converters.
    /// </summary>
    internal static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = SerializationDefaults.JsonMaxDepth,
        Converters =
        {
            // TolerantEnumConverterFactory replaces JsonStringEnumConverter for all
            // non-flags enums: it accepts case/underscore variants and, on failure,
            // throws a JsonException listing the valid values so LLM agents can
            // self-correct (vs the previous opaque "could not be converted" error).
            // Flags enums fall through to the EdgeKind-specific converter below.
            new TolerantEnumConverterFactory(),
            new BoolTolerantConverter(),
            new IntTolerantConverter(),
            new LongTolerantConverter(),
            new DecimalInvariantConverter(),
            new DoubleInvariantConverter(),
            new RawObjectConverter(),
            new UriTolerantConverter(),
            new ImmutableArrayEnumTolerantConverter<EdgeKind>(),
            new ImmutableArrayStringTolerantConverter()
        }
    };

    /// <inheritdoc />
    public T Deserialize<T>(Dictionary<string, object?> parameters) where T : class, new()
    {
        // NormalizeParameters handles VALUES only; we also snake_case the top-level
        // KEYS so JS callers using camelCase/PascalCase keys route to snake_case
        // properties. Without this, STJ's PropertyNameCaseInsensitive cannot
        // bridge "rootPath" → "root_path" and the field silently defaults.
        var normalizedValues = NormalizeParameters(parameters);
        var snakeKeyed = new Dictionary<string, object?>(normalizedValues.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in normalizedValues)
        {
            snakeKeyed[NormalizeKeyToSnakeCase(key)] = value;
        }
        var json = JsonSerializer.Serialize(snakeKeyed, SnakeCaseOptions);
        return JsonSerializer.Deserialize<T>(json, SnakeCaseOptions)
               ?? throw new JsonException($"Failed to deserialize parameters to {typeof(T).Name}");
    }

    /// <inheritdoc />
    public Dictionary<string, object?> Serialize<T>(T value) where T : class
    {
        var json = JsonSerializer.Serialize(value, SnakeCaseOptions);
        var raw = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, SnakeCaseOptions)
                  ?? [];
        // Unwrap JsonElement values: the struct is backed by a pooled buffer that
        // may be released before downstream consumers (notably Jint.JsValue.FromObject)
        // reflect on it — surfaces as ArgumentException("Offset and length out of bounds").
        // Keys are preserved verbatim (do NOT call NormalizeParameters here: user-supplied
        // dictionary keys inside nested dicts must not be rewritten).
        var materialised = new Dictionary<string, object?>(raw.Count);
        foreach (var (key, val) in raw)
        {
            materialised[key] = MaterialiseJsonElements(val);
        }
        return materialised;
    }

    private static object? MaterialiseJsonElements(object? value)
    {
        if (value is not JsonElement el) return value;
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new Dictionary<string, object?>();
                foreach (var p in el.EnumerateObject())
                    obj[p.Name] = MaterialiseJsonElements(p.Value);
                return obj;
            case JsonValueKind.Array:
                var arr = new List<object?>();
                foreach (var item in el.EnumerateArray())
                    arr.Add(MaterialiseJsonElements(item));
                // Return a CLR array (not List<>) so downstream JS bindings (Jint)
                // see a real JS Array with full Array.prototype.
                return arr.ToArray();
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.Number:
                if (el.TryGetInt32(out var i)) return i;
                if (el.TryGetInt64(out var l)) return l;
                if (el.TryGetDecimal(out var dec)) return dec;
                return el.GetDouble();
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Null: return null;
            default: return el.GetRawText();
        }
    }

    /// <inheritdoc />
    public Dictionary<string, object?> NormalizeParameters(Dictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var normalized = new Dictionary<string, object?>(parameters.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parameters)
        {
            var val = NormalizeValue(value);
            if (val is not null)
                normalized[key] = val;
        }
        return normalized;
    }

    /// <inheritdoc />
    public object? NormalizeValue(object? value)
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

        if (value is IDictionary<string, object> dict)
        {
            return NormalizeRawDictionary(dict);
        }

        if (value is IList list && value is not string)
        {
            return NormalizeRawList(list);
        }

        return value;
    }

    /// <inheritdoc />
    public string NormalizeKeyToSnakeCase(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length <= 1) return key;

        bool isCamelCase = char.IsLower(key[0]) && key.Any(char.IsUpper);
        bool isPascalCase = char.IsUpper(key[0]) && key.Any(char.IsLower) && !key.Contains('_', StringComparison.Ordinal);

        if (isCamelCase || isPascalCase)
        {
            return JsonNamingPolicy.SnakeCaseLower.ConvertName(key);
        }

        return key;
    }

    private Dictionary<string, object> NormalizeJsonObject(JsonElement element)
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

    private List<object> NormalizeJsonArray(JsonElement element)
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

    private Dictionary<string, object> NormalizeRawDictionary(IDictionary<string, object> dict)
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

    private List<object> NormalizeRawList(IList list)
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
}
