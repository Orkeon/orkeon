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
public sealed record ToolSchema(
    string Name,
    string Description,
    Dictionary<string, ParameterSchema> Parameters,
    Dictionary<string, object?>? Returns = null,
    Dictionary<string, object?>? Types = null
)
{
    /// <summary>
    /// False when the tool admits no parameter beyond <see cref="Parameters"/>: the formatters then publish
    /// <c>additionalProperties: false</c> on its input schema. True (the default) for an open one.
    /// </summary>
    public bool AllowsAdditionalParameters { get; init; } = true;

    /// <summary>
    /// The JSON Schema of the tool's input: an object with one property per parameter, the required ones, and
    /// <c>additionalProperties: false</c> when <see cref="AllowsAdditionalParameters"/> is false. It is what a
    /// provider's tool list and an MCP <c>tools/list</c> carry.
    /// </summary>
    /// <returns>The input schema, as a serialization-friendly dictionary.</returns>
    public Dictionary<string, object> ToInputJsonSchema() =>
        ParameterSchema.ObjectJsonSchema(Parameters, AllowsAdditionalParameters);

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
public sealed record ParameterSchema(
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
    /// <summary>
    /// The properties of each item when the parameter is an array of objects (the operations of a batch), by
    /// name; null for an array of scalars or a parameter that is no array. The formatters publish them as the
    /// items' <c>properties</c> and <c>required</c>, so the model learns the shape from the schema, not from
    /// the description alone (#210).
    /// </summary>
    public IReadOnlyDictionary<string, ParameterSchema>? ItemProperties { get; init; }

    /// <summary>
    /// The properties of an object parameter, by name, each with its own <see cref="Required"/>; null when it
    /// declares none. The formatters publish them as its <c>properties</c> and <c>required</c>.
    /// </summary>
    public IReadOnlyDictionary<string, ParameterSchema>? Properties { get; init; }

    /// <summary>
    /// The whole schema of an array's items - their type, description, enum, bounds, and an array or object of
    /// their own; null when only <see cref="ItemsType"/>, <see cref="ItemsFormat"/> and
    /// <see cref="ItemProperties"/> describe them. When it is set, the formatters publish it and ignore those three.
    /// </summary>
    public ParameterSchema? Items { get; init; }

    /// <summary>
    /// The alternatives a value matches at least one of (<c>anyOf</c>); null for none. A parameter that has
    /// them usually has no <see cref="Type"/> of its own.
    /// </summary>
    public IReadOnlyList<ParameterSchema>? AnyOf { get; init; }

    /// <summary>
    /// False when an object admits no property beyond <see cref="Properties"/>: published as
    /// <c>additionalProperties: false</c>. True (the default) for an open object; <see cref="Values"/>, when set,
    /// types the other properties instead.
    /// </summary>
    public bool AllowsAdditionalProperties { get; init; } = true;

    /// <summary>The least value a number may take (<c>minimum</c>); null for no bound.</summary>
    public double? Minimum { get; init; }

    /// <summary>The greatest value a number may take (<c>maximum</c>); null for no bound.</summary>
    public double? Maximum { get; init; }

    /// <summary>The bound a number must stay above (<c>exclusiveMinimum</c>); null for none.</summary>
    public double? ExclusiveMinimum { get; init; }

    /// <summary>The bound a number must stay below (<c>exclusiveMaximum</c>); null for none.</summary>
    public double? ExclusiveMaximum { get; init; }

    /// <summary>The fewest characters a string may hold (<c>minLength</c>); null for no bound.</summary>
    public int? MinLength { get; init; }

    /// <summary>The most characters a string may hold (<c>maxLength</c>); null for no bound.</summary>
    public int? MaxLength { get; init; }

    /// <summary>The regular expression a string matches (<c>pattern</c>); null for none.</summary>
    public string? Pattern { get; init; }

    /// <summary>The fewest items an array may hold (<c>minItems</c>); null for no bound.</summary>
    public int? MinItems { get; init; }

    /// <summary>The most items an array may hold (<c>maxItems</c>); null for no bound.</summary>
    public int? MaxItems { get; init; }

    /// <summary>The names of the required <see cref="ItemProperties"/>, in their order; empty for none.</summary>
    public IReadOnlyList<string> RequiredItemProperties() =>
        ItemProperties is null ? [] : ItemProperties.Where(p => p.Value.Required).Select(p => p.Key).ToList();

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
    /// The JSON Schema of a dictionary value, as <see cref="ToJsonSchema"/> writes it; empty when the value
    /// is untyped, which accepts any JSON value.
    /// </summary>
    /// <returns>The JSON Schema fragment of this value schema.</returns>
    public Dictionary<string, object> ToValueDocument() => ToJsonSchema();

    /// <summary>
    /// The JSON Schema of this parameter, as a provider's tool list and an MCP <c>tools/list</c> carry it:
    /// <c>type</c>, <c>description</c> (when not empty), <c>default</c>, <c>enum</c> (the names of
    /// <see cref="EnumMap"/> first), <c>format</c>, the bounds, the <c>items</c> of an array - <c>{}</c>, any
    /// JSON value, when they are not known: no array goes out without them -, the <c>properties</c> and
    /// <c>required</c> of an object, its <c>additionalProperties</c>, and <c>anyOf</c>. <see cref="Required"/>
    /// is the parent's to publish, in its <c>required</c>.
    /// </summary>
    /// <returns>The JSON Schema, as a serialization-friendly dictionary.</returns>
    public Dictionary<string, object> ToJsonSchema()
    {
        var schema = new Dictionary<string, object>();
        if (Type != null)
            schema["type"] = Type;
        if (!string.IsNullOrEmpty(Description))
            schema["description"] = Description;
        if (Default != null)
            schema["default"] = Default;
        if (EnumMap is { Count: > 0 })
            schema["enum"] = EnumMap.Keys.ToList();
        else if (Enum is { Count: > 0 })
            schema["enum"] = Enum;
        if (Format != null)
            schema["format"] = Format;
        WriteBounds(schema);

        if (Type == "array")
            schema["items"] = ItemsSchema() is { } items ? items.ToJsonSchema() : new Dictionary<string, object>();

        if (Properties is { Count: > 0 })
        {
            schema["properties"] = Properties.ToDictionary(p => p.Key, p => (object)p.Value.ToJsonSchema());
            if (RequiredNames(Properties) is { Count: > 0 } required)
                schema["required"] = required;
        }

        if (Values != null)
            schema["additionalProperties"] = Values.ToJsonSchema();
        else if (!AllowsAdditionalProperties)
            schema["additionalProperties"] = false;

        if (AnyOf is { Count: > 0 })
            schema["anyOf"] = AnyOf.Select(a => (object)a.ToJsonSchema()).ToList();

        return schema;
    }

    /// <summary>The JSON Schema of an object whose properties are <paramref name="properties"/>: a tool's input, an object parameter.</summary>
    internal static Dictionary<string, object> ObjectJsonSchema(
        IReadOnlyDictionary<string, ParameterSchema> properties, bool allowsAdditionalProperties)
    {
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties.ToDictionary(p => p.Key, p => (object)p.Value.ToJsonSchema()),
        };
        if (RequiredNames(properties) is { Count: > 0 } required)
            schema["required"] = required;
        if (!allowsAdditionalProperties)
            schema["additionalProperties"] = false;
        return schema;
    }

    /// <summary>
    /// The schema of an array's items: <see cref="Items"/> when set, else the one <see cref="ItemsType"/>,
    /// <see cref="ItemsFormat"/> and <see cref="ItemProperties"/> describe; null when nothing does.
    /// </summary>
    private ParameterSchema? ItemsSchema() =>
        Items ?? (ItemsType is null
            ? null
            : new ParameterSchema(ItemsType, "", false, Format: ItemsFormat) { Properties = ItemProperties });

    private static List<string> RequiredNames(IReadOnlyDictionary<string, ParameterSchema> properties) =>
        properties.Where(p => p.Value.Required).Select(p => p.Key).ToList();

    private void WriteBounds(Dictionary<string, object> schema)
    {
        if (Minimum is { } minimum) schema["minimum"] = minimum;
        if (Maximum is { } maximum) schema["maximum"] = maximum;
        if (ExclusiveMinimum is { } exclusiveMinimum) schema["exclusiveMinimum"] = exclusiveMinimum;
        if (ExclusiveMaximum is { } exclusiveMaximum) schema["exclusiveMaximum"] = exclusiveMaximum;
        if (MinLength is { } minLength) schema["minLength"] = minLength;
        if (MaxLength is { } maxLength) schema["maxLength"] = maxLength;
        if (Pattern != null) schema["pattern"] = Pattern;
        if (MinItems is { } minItems) schema["minItems"] = minItems;
        if (MaxItems is { } maxItems) schema["maxItems"] = maxItems;
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
        WriteBounds(doc);
        if (Items != null)
        {
            doc["items"] = Items.ToJsonSchema();
        }
        else if (ItemsType != null)
        {
            var items = new Dictionary<string, object> { ["type"] = ItemsType };
            if (ItemsFormat != null) items["format"] = ItemsFormat;
            // An array inside an array: what the inner one holds is not known, and `{}` says so.
            if (ItemsType == "array") items["items"] = new Dictionary<string, object>();
            if (ItemProperties is { Count: > 0 })
            {
                items["properties"] = ItemProperties.ToDictionary(p => p.Key, p => (object)p.Value.ToDocument());
                if (RequiredItemProperties() is { Count: > 0 } required)
                    items["required"] = required;
            }
            doc["items"] = items;
        }
        else if (Type == "array")
        {
            // An array always says what it holds: `{}`, any JSON value, when its item type is not known.
            doc["items"] = new Dictionary<string, object>();
        }
        if (Properties is { Count: > 0 })
            doc["properties"] = Properties.ToDictionary(p => p.Key, p => (object)p.Value.ToDocument());
        if (Values != null)
            doc["additionalProperties"] = Values.ToValueDocument();
        else if (!AllowsAdditionalProperties)
            doc["additionalProperties"] = false;
        if (AnyOf is { Count: > 0 })
            doc["anyOf"] = AnyOf.Select(a => (object)a.ToJsonSchema()).ToList();
        if (Example != null)
            doc["example"] = Example;

        return doc;
    }
}
