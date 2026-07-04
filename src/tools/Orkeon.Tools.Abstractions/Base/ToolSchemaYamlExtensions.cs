using Orkeon.Domain.Tools.Protocol;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Orkeon.Tools.Abstractions.Base;

/// <summary>
/// Extension methods for serializing/deserializing ToolSchema to/from YAML.
/// Enables round-trip: C# records → YAML → ToolSchema → validation.
/// </summary>
public static class ToolSchemaYamlExtensions
{
    private static readonly ISerializer s_serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .DisableAliases()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly IDeserializer s_deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    /// <summary>
    /// Serializes the ToolSchema to a YAML string.
    /// </summary>
    public static string ToYaml(this ToolSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var doc = schema.ToDocument();
        return s_serializer.Serialize(doc);
    }

    /// <summary>
    /// Deserializes a YAML string into a ToolSchema.
    /// The resulting schema can be used for parameter validation via ToolParameterValidator.
    /// </summary>
    public static ToolSchema FromYaml(string yaml)
    {
        var doc = s_deserializer.Deserialize<Dictionary<string, object>>(yaml)
            ?? throw new ArgumentException("Invalid YAML: empty document");

        var name = GetString(doc, "name") ?? throw new ArgumentException("Missing 'name' in YAML schema");
        var description = GetString(doc, "description") ?? "";

        // Parse parameters
        var parameters = new Dictionary<string, ParameterSchema>();
        if (doc.TryGetValue("parameters", out var paramsObj) && paramsObj is Dictionary<object, object> paramsDict)
        {
            foreach (var (keyObj, valueObj) in paramsDict)
            {
                var key = keyObj.ToString()!;
                if (valueObj is Dictionary<object, object> paramDef)
                {
                    parameters[key] = ParseParameterSchema(paramDef);
                }
            }
        }

        // Parse returns (kept as Dictionary<string, object?> for flexibility)
        Dictionary<string, object?>? returns = null;
        if (doc.TryGetValue("returns", out var returnsObj) && returnsObj is Dictionary<object, object> returnsDict)
        {
            returns = ConvertToStringKeyDict(returnsDict);
        }

        // Parse types
        Dictionary<string, object?>? types = null;
        if (doc.TryGetValue("types", out var typesObj) && typesObj is Dictionary<object, object> typesDict)
        {
            types = ConvertToStringKeyDict(typesDict);
        }

        return new ToolSchema(name, description, parameters, returns, types);
    }

    /// <summary>
    /// Validates a dictionary of parameters against a ToolSchema.
    /// Returns (isValid, errorMessage). Useful for validating requests against YAML-loaded schemas.
    /// </summary>
    public static (bool IsValid, string? Error) ValidateParameters(
        this ToolSchema schema, Dictionary<string, object> parameters)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(parameters);
        // Check required parameters
        foreach (var (paramName, paramSchema) in schema.Parameters)
        {
            if (paramSchema.Required && !parameters.ContainsKey(paramName))
                return (false, $"Required parameter '{paramName}' is missing");

            if (parameters.TryGetValue(paramName, out var value))
            {
                // Enum validation: EnumMap (C# enums) or Enum (explicit list)
                if (paramSchema.EnumMap is { Count: > 0 })
                {
                    var stringValue = value?.ToString();
                    var isValidName = paramSchema.EnumMap.ContainsKey(stringValue ?? "");
                    var isValidInt = int.TryParse(stringValue, out var intVal) && paramSchema.EnumMap.ContainsValue(intVal);
                    if (!isValidName && !isValidInt)
                        return (false, $"Parameter '{paramName}' must be one of: {string.Join(", ", paramSchema.EnumMap.Select(kv => $"{kv.Key}({kv.Value})"))}");
                }
                else if (paramSchema.Enum is { Count: > 0 })
                {
                    var stringValue = value?.ToString();
                    if (!paramSchema.Enum.Any(e => string.Equals(e?.ToString(), stringValue, StringComparison.Ordinal)))
                        return (false, $"Parameter '{paramName}' must be one of: {string.Join(", ", paramSchema.Enum)}");
                }
            }
        }

        return (true, null);
    }

    // ── Private parsing helpers ──────────────────────────────────────

    private static ParameterSchema ParseParameterSchema(Dictionary<object, object> dict)
    {
        var type = GetString(dict, "type") ?? "string";
        var description = GetString(dict, "description") ?? "";
        var required = GetBool(dict, "required");
        var format = GetString(dict, "format");
        var defaultValue = dict.TryGetValue("default", out var def) ? def : null;

        // Parse enum: can be a flat list (explicit) or a map (C# enum with name→value)
        List<object>? enumValues = null;
        Dictionary<string, int>? enumMap = null;
        if (dict.TryGetValue("enum", out var enumObj))
        {
            if (enumObj is List<object> enumList)
            {
                enumValues = enumList;
            }
            else if (enumObj is Dictionary<object, object> enumDict)
            {
                enumMap = [];
                foreach (var (k, v) in enumDict)
                {
                    if (int.TryParse(v?.ToString(), out var intVal))
                        enumMap[k.ToString()!] = intVal;
                }
            }
        }

        string? itemsType = null;
        string? itemsFormat = null;
        if (dict.TryGetValue("items", out var itemsObj) && itemsObj is Dictionary<object, object> itemsDict)
        {
            itemsType = GetString(itemsDict, "type");
            itemsFormat = GetString(itemsDict, "format");
        }

        // Parse example
        object? example = dict.TryGetValue("example", out var exObj) ? exObj : null;

        return new ParameterSchema(
            Type: type,
            Description: description,
            Required: required,
            Default: defaultValue,
            Enum: enumValues,
            Format: format,
            ItemsType: itemsType,
            ItemsFormat: itemsFormat,
            EnumMap: enumMap,
            Example: example
        );
    }

    private static string? GetString(Dictionary<object, object> dict, string key)
    {
        return dict.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static string? GetString(Dictionary<string, object> dict, string key)
    {
        return dict.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static bool GetBool(Dictionary<object, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value)) return false;
        if (value is bool b) return b;
        if (value is string s) return s.Equals("true", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    /// <summary>
    /// Recursively converts Dictionary&lt;object, object&gt; (YamlDotNet default)
    /// to Dictionary&lt;string, object&gt; with nested conversion.
    /// </summary>
    private static Dictionary<string, object?> ConvertToStringKeyDict(Dictionary<object, object> source)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in source)
        {
            result[key.ToString()!] = ConvertValue(value);
        }
        return result;
    }

    private static object ConvertValue(object value)
    {
        return value switch
        {
            Dictionary<object, object> dict => ConvertToStringKeyDict(dict),
            List<object> list => list.Select(ConvertValue).ToList(),
            _ => value
        };
    }
}
