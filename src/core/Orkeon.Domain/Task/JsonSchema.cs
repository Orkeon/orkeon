using System.Reflection;
using System.Text.Json;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Domain.Task;

/// <summary>
/// Represents a JSON schema for validation.
/// </summary>
public sealed class JsonSchema
{
    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true, MaxDepth = SerializationDefaults.JsonMaxDepth };

    private readonly JsonDocument _schema;

    /// <summary>
    /// Gets the schema as a JSON string.
    /// </summary>
    public string Schema { get; }

    /// <summary>Initializes a new instance of <see cref="JsonSchema"/>.</summary>
    /// <param name="schema">The JSON schema string.</param>
    private JsonSchema(string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        try
        {
            _schema = JsonDocument.Parse(schema);
            Schema = schema;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Invalid JSON schema", nameof(schema), ex);
        }
    }

    /// <summary>
    /// Creates a new <see cref="JsonSchema"/> from a JSON schema string.
    /// </summary>
    /// <param name="schema">The JSON schema string.</param>
    /// <returns>A new <see cref="JsonSchema"/> instance.</returns>
    public static JsonSchema From(string schema) => new(schema);

    /// <summary>
    /// Validates a JSON string against this schema.
    /// </summary>
    public bool Validate(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            return ValidateAgainstSchema(document.RootElement, _schema.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool ValidateAgainstSchema(JsonElement data, JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var typeElement))
            return true;

        var schemaType = typeElement.GetString();

        return schemaType switch
        {
            "object" => ValidateObject(data, schema),
            "array" => ValidateArray(data, schema),
            "string" => data.ValueKind == JsonValueKind.String,
            "number" => data.ValueKind == JsonValueKind.Number,
            "integer" => data.ValueKind == JsonValueKind.Number && data.TryGetInt64(out _),
            "boolean" => data.ValueKind == JsonValueKind.True || data.ValueKind == JsonValueKind.False,
            "null" => data.ValueKind == JsonValueKind.Null,
            _ => true
        };
    }

    private bool ValidateObject(JsonElement data, JsonElement schema)
    {
        if (data.ValueKind != JsonValueKind.Object)
            return false;

        if (!ValidateRequiredProperties(data, schema))
            return false;

        if (!ValidateObjectProperties(data, schema))
            return false;

        return true;
    }

    /// <summary>
    /// Validates that all required properties are present in the data.
    /// </summary>
    private static bool ValidateRequiredProperties(JsonElement data, JsonElement schema)
    {
        if (!schema.TryGetProperty("required", out var required))
            return true;

        foreach (var prop in required.EnumerateArray())
        {
            var propName = prop.GetString();
            if (propName != null && !data.TryGetProperty(propName, out _))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Validates each property in the data against its schema definition.
    /// </summary>
    private bool ValidateObjectProperties(JsonElement data, JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out var properties))
            return true;

        var disallowAdditional = schema.TryGetProperty("additionalProperties", out var additional) &&
                                  additional.ValueKind == JsonValueKind.False;

        foreach (var prop in data.EnumerateObject())
        {
            if (properties.TryGetProperty(prop.Name, out var propSchema))
            {
                if (!ValidateAgainstSchema(prop.Value, propSchema))
                    return false;
            }
            else if (disallowAdditional)
            {
                return false;
            }
        }

        return true;
    }

    private bool ValidateArray(JsonElement data, JsonElement schema)
    {
        if (data.ValueKind != JsonValueKind.Array)
            return false;

        if (schema.TryGetProperty("items", out var items))
        {
            foreach (var element in data.EnumerateArray())
            {
                if (!ValidateAgainstSchema(element, items))
                    return false;
            }
        }

        if (schema.TryGetProperty("minItems", out var minItems) &&
            data.GetArrayLength() < minItems.GetInt32())
            return false;

        if (schema.TryGetProperty("maxItems", out var maxItems) &&
            data.GetArrayLength() > maxItems.GetInt32())
            return false;

        return true;
    }

    /// <summary>
    /// Creates a JSON schema from a type.
    /// </summary>
    public static JsonSchema FromType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var schema = GenerateSchemaForType(type);
        return From(JsonSerializer.Serialize(schema, s_indentedOptions));
    }

    private static Dictionary<string, object> GenerateSchemaForType(Type type)
    {
        if (TryGeneratePrimitiveSchema(type, out var primitiveSchema))
            return primitiveSchema;

        if (IsArrayOrListType(type))
            return GenerateArraySchema(type);

        if (type.IsClass && type != typeof(string))
            return GenerateObjectSchema(type);

        return [];
    }

    /// <summary>
    /// Tries to generate a schema for primitive types (string, int, bool, etc.).
    /// Returns true if the type is a recognized primitive.
    /// </summary>
    private static bool TryGeneratePrimitiveSchema(Type type, out Dictionary<string, object> schema)
    {
        schema = [];

        if (type == typeof(string))
        {
            schema["type"] = "string";
            return true;
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(short))
        {
            schema["type"] = "integer";
            return true;
        }

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            schema["type"] = "number";
            return true;
        }

        if (type == typeof(bool))
        {
            schema["type"] = "boolean";
            return true;
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            schema["type"] = "string";
            schema["format"] = "date-time";
            return true;
        }

        if (type == typeof(Guid))
        {
            schema["type"] = "string";
            schema["format"] = "uuid";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether the type is an array or a generic List.
    /// </summary>
    private static bool IsArrayOrListType(Type type)
    {
        return type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>));
    }

    /// <summary>
    /// Generates a JSON schema for array/list types.
    /// </summary>
    private static Dictionary<string, object> GenerateArraySchema(Type type)
    {
        var schema = new Dictionary<string, object> { ["type"] = "array" };
        var elementType = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
        if (elementType != null)
        {
            schema["items"] = GenerateSchemaForType(elementType);
        }

        return schema;
    }

    /// <summary>
    /// Generates a JSON schema for object (class) types by reflecting over public properties.
    /// </summary>
    private static Dictionary<string, object> GenerateObjectSchema(Type type)
    {
        var schema = new Dictionary<string, object> { ["type"] = "object" };
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite)
                continue;

            properties[prop.Name] = GenerateSchemaForType(prop.PropertyType);

            if (!IsNullableType(prop.PropertyType))
            {
                required.Add(prop.Name);
            }
        }

        schema["properties"] = properties;
        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema;
    }

    private static bool IsNullableType(Type type)
    {
        return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
    }

    /// <inheritdoc />
    public override string ToString() => Schema;
}
