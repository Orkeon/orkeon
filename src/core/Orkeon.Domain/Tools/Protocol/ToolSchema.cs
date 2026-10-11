using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orkeon.Domain.Tools.Protocol;

/// <summary>
/// Schema definition for a tool, including its name, description, parameters, and return type.
/// </summary>
/// <param name="Name">The tool name.</param>
/// <param name="Description">The tool description.</param>
/// <param name="Parameters">The parameter schemas keyed by parameter name.</param>
/// <param name="Returns">Optional return type schema.</param>
/// <param name="Types">Optional type definitions used in parameters or returns.</param>
public record ToolSchema(
    string Name,
    string Description,
    Dictionary<string, ParameterSchema> Parameters,
    Dictionary<string, object?>? Returns = null,
    Dictionary<string, object?>? Types = null
)
{
    /// <summary>
    /// Converts the schema to a serialization-friendly dictionary (ordered, null-free).
    /// Suitable for YAML or JSON serialization.
    /// </summary>
    public Dictionary<string, object> ToDocument()
    {
        var doc = new Dictionary<string, object>
        {
            ["name"] = Name,
            ["description"] = Description
        };

        if (Parameters.Count > 0)
        {
            var parameters = new Dictionary<string, object>();
            foreach (var (key, param) in Parameters)
            {
                parameters[key] = param.ToDocument();
            }
            doc["parameters"] = parameters;
        }

        if (Returns is { Count: > 0 })
            doc["returns"] = Returns;

        if (Types is { Count: > 0 })
            doc["types"] = Types;

        // Generate example from parameter examples
        var example = GenerateExample();
        if (example.Count > 0)
            doc["example"] = example;

        return doc;
    }

    /// <summary>
    /// Generates a complete usage example from parameter Example values.
    /// Only includes parameters that have an Example defined.
    /// </summary>
    /// <returns>A dictionary of parameter names to their example values.</returns>
    public Dictionary<string, object> GenerateExample()
    {
        var example = new Dictionary<string, object>();
        foreach (var (key, param) in Parameters)
        {
            if (param.Example != null)
                example[key] = param.Example;
        }
        return example;
    }
}

/// <summary>
/// Schema definition for a single tool parameter.
/// </summary>
/// <param name="Type">The parameter type (e.g. "string", "integer"); null means any JSON value.</param>
/// <param name="Description">The parameter description.</param>
/// <param name="Required">Whether the parameter is required.</param>
/// <param name="Default">The optional default value.</param>
/// <param name="Enum">Optional list of allowed values.</param>
/// <param name="Format">Optional format hint (e.g. "date-time").</param>
/// <param name="ItemsType">For array types, the element type.</param>
/// <param name="ItemsFormat">For array types, the element format.</param>
/// <param name="EnumMap">Optional enum name-to-value mapping for C# enums.</param>
/// <param name="Example">Optional example value.</param>
/// <param name="Values">For a dictionary, the schema of its values.</param>
public record ParameterSchema(
    string? Type,
    string Description,
    bool Required,
    object? Default = null,
    IReadOnlyList<object>? Enum = null,
    string? Format = null,
    string? ItemsType = null,
    string? ItemsFormat = null,
    Dictionary<string, int>? EnumMap = null,
    object? Example = null,
    ParameterSchema? Values = null
)
{
    /// <summary>True when this parameter is a dictionary (its values are typed by <see cref="Values"/>).</summary>
    public bool IsMap => Values is not null;

    /// <summary>Reads a key/value array sent for a dictionary parameter as the equivalent object.</summary>
    /// <param name="value">The value received for this parameter.</param>
    /// <returns>The equivalent object for a key/value array; any other value unchanged.</returns>
    public object? ReadAsMap(object? value)
    {
        if (!IsMap || value is null or string || !TryAsArray(value, out var array))
        {
            return value;
        }

        var map = new JsonObject();
        foreach (var item in array)
        {
            if (!TryAsPair(item, out var key, out var itemValue))
            {
                return value;
            }

            map[key] = itemValue?.DeepClone();
        }

        return map;
    }

    /// <summary>True for a non-empty array whose items are exactly <c>{key: string, value: any}</c>.</summary>
    /// <param name="value">The value to inspect.</param>
    /// <returns>Whether the value is a key/value array.</returns>
    public static bool IsKeyValueArray(object? value) =>
        value is not (null or string)
        && TryAsArray(value, out var array)
        && array.Count > 0
        && array.All(item => TryAsPair(item, out _, out _));

    private static bool TryAsArray(object value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JsonArray? array)
    {
        array = null;
        if (value is JsonElement { ValueKind: not JsonValueKind.Array } or JsonNode and not JsonArray or IDictionary)
        {
            return false;
        }

        if (value is not (JsonElement or JsonArray or IEnumerable))
        {
            return false;
        }

        try
        {
            array = JsonSerializer.SerializeToNode(value) as JsonArray;
        }
        catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
        {
            return false;
        }

        return array is not null;
    }

    private static bool TryAsPair(
        JsonNode? item,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? key,
        out JsonNode? itemValue)
    {
        key = null;
        itemValue = null;
        if (item is not JsonObject { Count: 2 } pair
            || !pair.TryGetPropertyValue("key", out var keyNode)
            || !pair.TryGetPropertyValue("value", out itemValue)
            || keyNode is not JsonValue keyValue
            || !keyValue.TryGetValue(out key))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// The bare JSON Schema of a dictionary value (<c>type</c> and <c>format</c> only);
    /// empty when the value is untyped, which accepts any JSON value.
    /// </summary>
    /// <returns>The JSON Schema fragment of this value schema.</returns>
    public Dictionary<string, object> ToValueDocument()
    {
        var doc = new Dictionary<string, object>();
        if (Type != null)
            doc["type"] = Type;
        if (Format != null)
            doc["format"] = Format;
        return doc;
    }

    /// <summary>
    /// Converts to a serialization-friendly dictionary (null-free).
    /// </summary>
    /// <returns>A dictionary representation of this parameter schema.</returns>
    public Dictionary<string, object> ToDocument()
    {
        var doc = new Dictionary<string, object>
        {
            ["description"] = Description,
            ["required"] = Required
        };

        if (Type != null)
            doc["type"] = Type;

        if (Format != null)
            doc["format"] = Format;
        if (Default != null)
            doc["default"] = Default;
        // EnumMap (C# enums with name→value mapping) takes priority over flat Enum list
        if (EnumMap is { Count: > 0 })
            doc["enum"] = EnumMap;
        else if (Enum is { Count: > 0 })
            doc["enum"] = Enum;
        if (ItemsType != null)
        {
            var items = new Dictionary<string, object> { ["type"] = ItemsType };
            if (ItemsFormat != null) items["format"] = ItemsFormat;
            doc["items"] = items;
        }
        if (Values != null)
            doc["additionalProperties"] = Values.ToValueDocument();
        if (Example != null)
            doc["example"] = Example;

        return doc;
    }
}
