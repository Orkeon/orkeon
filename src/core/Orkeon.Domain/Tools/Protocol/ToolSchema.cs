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
/// <param name="Type">The parameter type (e.g. "string", "integer").</param>
/// <param name="Description">The parameter description.</param>
/// <param name="Required">Whether the parameter is required.</param>
/// <param name="Default">The optional default value.</param>
/// <param name="Enum">Optional list of allowed values.</param>
/// <param name="Format">Optional format hint (e.g. "date-time").</param>
/// <param name="ItemsType">For array types, the element type.</param>
/// <param name="ItemsFormat">For array types, the element format.</param>
/// <param name="EnumMap">Optional enum name-to-value mapping for C# enums.</param>
/// <param name="Example">Optional example value.</param>
public record ParameterSchema(
    string Type,
    string Description,
    bool Required,
    object? Default = null,
    IReadOnlyList<object>? Enum = null,
    string? Format = null,
    string? ItemsType = null,
    string? ItemsFormat = null,
    Dictionary<string, int>? EnumMap = null,
    object? Example = null
)
{
    /// <summary>
    /// Converts to a serialization-friendly dictionary (null-free).
    /// </summary>
    /// <returns>A dictionary representation of this parameter schema.</returns>
    public Dictionary<string, object> ToDocument()
    {
        var doc = new Dictionary<string, object>
        {
            ["type"] = Type,
            ["description"] = Description,
            ["required"] = Required
        };

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
        if (Example != null)
            doc["example"] = Example;

        return doc;
    }
}
