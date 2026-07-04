using System.Text.Json;
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
    {
        if (paramSchema.Required && !parameters.ContainsKey(paramName))
        {
            return ValidationResult.Failed($"Required parameter '{paramName}' is missing");
        }

        if (!parameters.TryGetValue(paramName, out var value) || value is null)
        {
            return ValidationResult.Success();
        }

        // Basic type validation
        if (!IsValidType(value, paramSchema.Type))
        {
            return ValidationResult.Failed($"Parameter '{paramName}' has invalid type. Expected: {paramSchema.Type}");
        }

        // Enum validation: check EnumMap (C# enums) or Enum (explicit list)
        return ValidateEnum(paramName, paramSchema, value);
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
        else if (paramSchema.Enum != null && paramSchema.Enum.Count > 0 && !paramSchema.Enum.Contains(value))
        {
            return ValidationResult.Failed($"Parameter '{paramName}' must be one of: {string.Join(", ", paramSchema.Enum)}");
        }

        return ValidationResult.Success();
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
    public static bool IsValidType(object value, string expectedType)
    {
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
        "integer" => je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out _),
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
        "integer" => value is int or long
                     || (value is string istr && long.TryParse(istr, out _)),
        "boolean" => value is bool
                     || (value is string bs && (bs is "true" or "false" or "True" or "False" or "0" or "1")),
        "array" => value is System.Collections.IEnumerable and not string,
        "object" => value is IDictionary<string, object> or JsonElement { ValueKind: JsonValueKind.Object },
        _ => true // Unknown types pass validation
    };
}
