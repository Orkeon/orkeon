using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Tools.Abstractions.Base;

/// <summary>
/// Handles validation of tool parameters against a tool's schema.
/// Extracted from ToolBase to follow Single Responsibility Principle.
/// </summary>
internal static class ToolParameterValidator
{
    /// <summary>
    /// Validates parameters against the tool's schema.
    /// </summary>
    public static ValidationResult ValidateParameters(Dictionary<string, object?> parameters, ToolSchema? schema)
    {
        if (schema == null || schema.Parameters == null)
            return ValidationResult.Success();

        // Check required parameters
        foreach (var (paramName, paramSchema) in schema.Parameters)
        {
            var result = ValidateParameter(parameters, paramName, paramSchema);
            if (!result.IsValid)
            {
                return result;
            }
        }

        return ValidationResult.Success();
    }

    private static ValidationResult ValidateParameter(
        Dictionary<string, object?> parameters,
        string paramName,
        ParameterSchema paramSchema)
        => ValidateParameter(parameters, paramName, paramName, paramSchema);

    /// <param name="parameters">The parameters, or the properties of one item of an array.</param>
    /// <param name="paramName">The key read.</param>
    /// <param name="displayName">The name the message carries: the key, or <c>operations[0].path</c> for an item's.</param>
    /// <param name="paramSchema">The schema of the parameter.</param>
    private static ValidationResult ValidateParameter(
        IReadOnlyDictionary<string, object?> parameters,
        string paramName,
        string displayName,
        ParameterSchema paramSchema)
    {
        if (paramSchema.Required && !parameters.ContainsKey(paramName))
        {
            return ValidationResult.Failed($"Required parameter '{displayName}' is missing");
        }

        if (!parameters.TryGetValue(paramName, out var value) || value is null)
        {
            return ValidationResult.Success();
        }

        if (paramSchema.IsMap)
        {
            return ValidateMap(displayName, paramSchema, value);
        }

        // Basic type validation
        if (!IsValidType(value, paramSchema.Type))
        {
            return ValidationResult.Failed($"Parameter '{displayName}' has invalid type. Expected: {paramSchema.Type}");
        }

        // Enum validation: check EnumMap (C# enums) or Enum (explicit list)
        var result = ValidateEnum(displayName, paramSchema, value);
        return result.IsValid ? ValidateItems(displayName, paramSchema, value) : result;
    }

    /// <summary>
    /// The items of an array of objects (#210): each item's properties are judged as the parameters are -
    /// required, type, enum - and named by their place, <c>operations[0].path</c>. An item that is no object
    /// is left to the tool.
    /// </summary>
    private static ValidationResult ValidateItems(string displayName, ParameterSchema paramSchema, object value)
    {
        if (paramSchema.ItemProperties is not { Count: > 0 } properties)
        {
            return ValidationResult.Success();
        }

        var index = 0;
        foreach (var item in EnumerateItems(value))
        {
            if (TryAsParameterMap(item, out var map))
            {
                foreach (var (name, schema) in properties)
                {
                    var result = ValidateParameter(map, name, $"{displayName}[{index}].{name}", schema);
                    if (!result.IsValid)
                    {
                        return result;
                    }
                }
            }

            index++;
        }

        return ValidationResult.Success();
    }

    private static IEnumerable<object?> EnumerateItems(object value) => value switch
    {
        JsonElement { ValueKind: JsonValueKind.Array } element => element.EnumerateArray().Cast<object?>(),
        string => [],
        System.Collections.IEnumerable items => items.Cast<object?>(),
        _ => [],
    };

    /// <summary>An item as its properties: a dictionary as the tool-call parser hands it, or a JSON object.</summary>
    private static bool TryAsParameterMap(object? item, out IReadOnlyDictionary<string, object?> map)
    {
        switch (item)
        {
            case IReadOnlyDictionary<string, object?> readOnly:
                map = readOnly;
                return true;
            case IDictionary<string, object?> dictionary:
                map = new Dictionary<string, object?>(dictionary);
                return true;
            case JsonElement { ValueKind: JsonValueKind.Object } element:
                map = element.EnumerateObject().ToDictionary(
                    p => p.Name,
                    p => p.Value.ValueKind == JsonValueKind.Null ? null : (object?)p.Value);
                return true;
            default:
                map = new Dictionary<string, object?>();
                return false;
        }
    }

    private static ValidationResult ValidateMap(string paramName, ParameterSchema paramSchema, object value)
    {
        JsonNode? node;
        try
        {
            node = JsonSerializer.SerializeToNode(value);
        }
        catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
        {
            node = null;
        }

        var entries = new List<(string Key, JsonNode? Value)>();
        if (node is JsonObject map)
        {
            entries.AddRange(map.Select(p => (p.Key, p.Value)));
        }
        else if (node is JsonArray array)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in array)
            {
                if (!ParameterSchema.IsKeyValueArray(new JsonArray(item?.DeepClone())))
                {
                    return ValidationResult.Failed($"Parameter '{paramName}' must be an object or an array of {{key, value}} items");
                }

                var key = item!["key"]!.GetValue<string>();
                if (!keys.Add(key))
                {
                    return ValidationResult.Failed($"Parameter '{paramName}' repeats the key '{key}'");
                }

                entries.Add((key, item["value"]));
            }
        }
        else
        {
            return ValidationResult.Failed($"Parameter '{paramName}' has invalid type. Expected: object");
        }

        foreach (var (key, entry) in entries)
        {
            if (entry is not null && !IsValidType(JsonSerializer.SerializeToElement(entry), paramSchema.Values!.Type))
            {
                return ValidationResult.Failed($"Parameter '{paramName}': the value of '{key}' has invalid type. Expected: {paramSchema.Values.Type}");
            }
        }

        return ValidationResult.Success();
    }

    private static ValidationResult ValidateEnum(string paramName, ParameterSchema paramSchema, object value)
    {
        if (paramSchema.EnumMap is { Count: > 0 })
        {
            var stringValue = value.ToString();
            // Accept both name ("Red"/"red") and integer value (0), case-insensitive
            var isValidName = paramSchema.EnumMap.Keys.Any(k => string.Equals(k, stringValue, StringComparison.OrdinalIgnoreCase));
            var isValidInt = int.TryParse(stringValue, out var intVal) && paramSchema.EnumMap.ContainsValue(intVal);
            if (!isValidName && !isValidInt)
            {
                return ValidationResult.Failed($"Parameter '{paramName}' must be one of: {string.Join(", ", paramSchema.EnumMap.Select(kv => $"{kv.Key}({kv.Value})"))}");
            }
        }
        else if (paramSchema.Enum != null && paramSchema.Enum.Count > 0 && !IsEnumValue(paramSchema.Enum, value))
        {
            return ValidationResult.Failed($"Parameter '{paramName}' must be one of: {string.Join(", ", paramSchema.Enum)}");
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Whether a value is one of a flat enum list, as listed. A JSON string is read as the string it
    /// holds, as <see cref="IsValidType"/> reads a <see cref="JsonElement"/> the upstream left wrapped.
    /// </summary>
    private static bool IsEnumValue(IReadOnlyList<object> allowed, object value)
    {
        var candidate = value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : value;
        return candidate is not null && allowed.Contains(candidate);
    }

    /// <summary>
    /// Validates string input against tool parameters.
    /// </summary>
    public static ValidationResult ValidateInput(string input)
    {
        try
        {
            // Try to parse as JSON if the tool expects structured input
            if (!string.IsNullOrWhiteSpace(input) && input.TrimStart().StartsWith('{'))
            {
                using var _ = JsonDocument.Parse(input);
                // Basic validation - check if it's valid JSON
                return ValidationResult.Success();
            }

            // For simple string inputs, just check if not empty
            if (string.IsNullOrWhiteSpace(input))
            {
                return ValidationResult.Failed("Input cannot be empty");
            }

            return ValidationResult.Success();
        }
        catch (JsonException ex)
        {
            return ValidationResult.Failed($"Invalid JSON input: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if a value matches the expected type.
    /// </summary>
    public static bool IsValidType(object value, string? expectedType)
    {
        if (expectedType == null) return true;

        // Defense-in-depth: unwrap JsonElement to .NET primitive before checking type.
        // This handles cases where upstream deserialization did not fully unwrap values.
#pragma warning disable CA1308 // expectedType is normalized to the lowercase switch keys ("string", "number", ...), a required match form, not a comparison normalization
        return value is JsonElement je
            ? IsValidJsonElementType(je, expectedType.ToLowerInvariant())
            : IsValidClrType(value, expectedType.ToLowerInvariant());
#pragma warning restore CA1308
    }

    private static bool IsValidJsonElementType(JsonElement je, string expectedType) => expectedType switch
    {
        "string" => je.ValueKind == JsonValueKind.String,
        "number" => je.ValueKind == JsonValueKind.Number,
        // 20.0 and 2e1 are integers too, as on the CLR side below.
        "integer" => je.ValueKind == JsonValueKind.Number
                     && (je.TryGetInt64(out _) || (je.TryGetDouble(out var whole) && IsWholeNumber(whole))),
        "boolean" => je.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "array" => je.ValueKind == JsonValueKind.Array,
        "object" => je.ValueKind == JsonValueKind.Object,
        _ => true
    };

    private static bool IsValidClrType(object value, string expectedType) => expectedType switch
    {
        "string" => value is string,
        "number" => value is int or long or float or double or decimal
                    || (value is string ns && double.TryParse(ns, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _)),
        // A whole number held as a floating-point value is an integer (JSON Schema's own
        // definition): every number a script passes arrives as a double (Jint), so 20 is 20.0.
        "integer" => value is int or long or short or byte or sbyte or ushort or uint
                     || (value is double d && IsWholeNumber(d))
                     || (value is float f && IsWholeNumber(f))
                     || (value is decimal m && decimal.Truncate(m) == m && m >= long.MinValue && m <= long.MaxValue)
                     || (value is string istr && long.TryParse(istr, out _)),
        "boolean" => value is bool
                     || (value is string bs && (bs is "true" or "false" or "True" or "False" or "0" or "1")),
        "array" => value is System.Collections.IEnumerable and not string,
        "object" => value is System.Collections.IDictionary or IDictionary<string, object?> or JsonObject or JsonElement { ValueKind: JsonValueKind.Object },
        _ => true // Unknown types pass validation
    };

    private static bool IsWholeNumber(double value) =>
        double.IsFinite(value) && Math.Floor(value) == value && value >= long.MinValue && value <= long.MaxValue;
}
