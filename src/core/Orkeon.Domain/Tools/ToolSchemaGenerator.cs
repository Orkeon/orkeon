using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Domain.Tools;

/// <summary>
/// Generates ToolSchema from TRequest/TResponse records using [FieldSchema] and [ReturnSchema] attributes.
/// Type and Required are inferred from C# property types by default (OpenAPI/JSON Schema aligned).
/// Non-primitive object types are extracted into a "types" section and referenced via "$ref".
/// Results are cached per type pair.
/// </summary>
public static class ToolSchemaGenerator
{
    private static readonly ConcurrentDictionary<(Type, string, string), ToolSchema> s_schemaCache = new();
    private static readonly ConcurrentDictionary<Type, (Dictionary<string, object?> Returns, Dictionary<string, object?> Types)> s_returnsCache = new();
    // NullabilityInfoContext is not thread-safe (documented). A new instance per call is cheap
    // and avoids concurrent-access exceptions that would silently fall back to Required=false.
    [ThreadStatic]
    private static NullabilityInfoContext? t_nullabilityContext;

    /// <summary>
    /// Generates a ToolSchema from a TRequest type's [FieldSchema] attributes.
    /// </summary>
    public static ToolSchema GenerateSchema<TRequest>(string name, string description) where TRequest : class
    {
        return GenerateSchema(typeof(TRequest), name, description);
    }

    /// <summary>
    /// Generates a ToolSchema from a request type's [FieldSchema] attributes.
    /// </summary>
    public static ToolSchema GenerateSchema(Type requestType, string name, string description)
    {
        var key = (requestType, name, description);
        return s_schemaCache.GetOrAdd(key, _ => BuildSchema(requestType, name, description));
    }

    /// <summary>
    /// Generates returns dictionary and types dictionary from a TResponse type.
    /// Non-primitive object types are extracted into the types dictionary and referenced via "$ref".
    /// </summary>
    public static (Dictionary<string, object?> Returns, Dictionary<string, object?> Types) GenerateReturnsWithTypes<TResponse>() where TResponse : class
    {
        return GenerateReturnsWithTypes(typeof(TResponse));
    }

    /// <summary>
    /// Generates returns dictionary and types dictionary from a response type.
    /// </summary>
    public static (Dictionary<string, object?> Returns, Dictionary<string, object?> Types) GenerateReturnsWithTypes(Type responseType)
    {
        return s_returnsCache.GetOrAdd(responseType, BuildReturnsWithTypes);
    }

    /// <summary>
    /// Generates returns dictionary from a TResponse type (backward-compatible convenience method).
    /// For full output including type definitions, use GenerateReturnsWithTypes instead.
    /// </summary>
    public static Dictionary<string, object?> GenerateReturns<TResponse>() where TResponse : class
    {
        return GenerateReturnsWithTypes<TResponse>().Returns;
    }

    /// <summary>
    /// Generates returns dictionary from a response type (backward-compatible convenience method).
    /// </summary>
    public static Dictionary<string, object?> GenerateReturns(Type responseType)
    {
        return GenerateReturnsWithTypes(responseType).Returns;
    }

    /// <summary>
    /// Maps a C# type to its JSON Schema type string.
    /// </summary>
    public static string InferSchemaType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string)) return "string";
        if (underlying == typeof(bool)) return "boolean";
        if (underlying == typeof(int) || underlying == typeof(long) ||
            underlying == typeof(short) || underlying == typeof(byte) ||
            underlying == typeof(uint) || underlying == typeof(ulong) ||
            underlying == typeof(ushort) || underlying == typeof(sbyte))
            return "integer";
        if (underlying == typeof(double) || underlying == typeof(float))
            return "number";
        // decimal → string for precision safety
        if (underlying == typeof(decimal)) return "string";
        // Date/time types → string
        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) ||
            underlying == typeof(DateOnly) || underlying == typeof(TimeOnly) ||
            underlying == typeof(TimeSpan))
            return "string";
        // Guid → string
        if (underlying == typeof(Guid)) return "string";
        // Uri → string (the LLM fills URL parameters as plain strings on the wire, and
        // System.Text.Json round-trips Uri↔string natively on deserialize/serialize)
        if (underlying == typeof(Uri)) return "string";
        // Enum → string
        if (underlying.IsEnum) return "string";
        // Dictionary<,> is semantically an object (key-value map), not an array
        if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            return "object";
        // Collections → array
        if (IsCollectionType(underlying)) return "array";

        return "object";
    }

    /// <summary>
    /// Maps a C# type to its OpenAPI format string, or null if no specific format.
    /// </summary>
    public static string? InferSchemaFormat(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        var integerFormat = TryInferIntegerFormat(underlying);
        if (integerFormat is not null) return integerFormat;

        if (underlying == typeof(double)) return "double";
        if (underlying == typeof(float)) return "float";
        if (underlying == typeof(decimal)) return "decimal";
        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
            return "date-time";
        if (underlying == typeof(DateOnly)) return "date";
        if (underlying == typeof(TimeOnly)) return "time";
        if (underlying == typeof(TimeSpan)) return "duration";
        if (underlying == typeof(Guid)) return "uuid";
        if (underlying == typeof(Uri)) return "uri";

        return null;
    }

    /// <summary>
    /// Maps the integral C# types to their OpenAPI format string, or null when the
    /// type is not an integral type handled here.
    /// </summary>
    private static string? TryInferIntegerFormat(Type underlying)
    {
        if (underlying == typeof(int) || underlying == typeof(uint)) return "int32";
        if (underlying == typeof(long) || underlying == typeof(ulong)) return "int64";
        if (underlying == typeof(short) || underlying == typeof(ushort) ||
            underlying == typeof(byte) || underlying == typeof(sbyte))
            return "int32";

        return null;
    }

    /// <summary>
    /// Determines if a property is required based on nullability.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Reflection fault barrier: NullabilityInfoContext.Create can throw on edge-case metadata; any failure falls back to Required=false rather than aborting schema generation.")]
    public static bool InferRequired(PropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var type = property.PropertyType;

        // Value types: Nullable<T> = optional, else required
        if (type.IsValueType)
            return Nullable.GetUnderlyingType(type) == null;

        // Reference types: use NullabilityInfoContext (thread-static — not thread-safe to share)
        try
        {
            var ctx = t_nullabilityContext ??= new NullabilityInfoContext();
            var info = ctx.Create(property);
            return info.WriteState != NullabilityState.Nullable;
        }
        catch
        {
            // If nullability info unavailable, default to not required
            return false;
        }
    }

    /// <summary>
    /// Gets enum values as a dictionary mapping literal names to integer values.
    /// Produces YAML like: enum: { Red: 0, Green: 1, Blue: 2 }
    /// </summary>
    public static Dictionary<string, int> GetEnumValues(Type enumType)
    {
        ArgumentNullException.ThrowIfNull(enumType);
        var underlying = Nullable.GetUnderlyingType(enumType) ?? enumType;
        if (!underlying.IsEnum) return [];

        var values = new Dictionary<string, int>();
        foreach (var name in System.Enum.GetNames(underlying))
        {
            values[name] = Convert.ToInt32(System.Enum.Parse(underlying, name), CultureInfo.InvariantCulture);
        }
        return values;
    }

    // ── Private helpers ──────────────────────────────────────────────

    private static ToolSchema BuildSchema(Type requestType, string name, string description)
    {
        var parameters = new Dictionary<string, ParameterSchema>();

        foreach (var prop in requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = prop.GetCustomAttribute<FieldSchemaAttribute>();
            if (attr == null) continue;

            var snakeName = ToSnakeCase(prop.Name);
            var propType = prop.PropertyType;
            var underlying = Nullable.GetUnderlyingType(propType) ?? propType;

            var schemaType = attr.Type ?? InferSchemaType(propType);
            var format = attr.Format ?? InferSchemaFormat(propType);
            var required = attr.Required ?? InferRequired(prop);

            // Enum values: from attribute (flat list) or inferred from C# enum type (name→value map)
            List<object>? enumValues = null;
            Dictionary<string, int>? enumMap = null;
            if (attr.Enum is { Length: > 0 })
            {
                enumValues = attr.Enum.Cast<object>().ToList();
            }
            else if (underlying.IsEnum)
            {
                enumMap = GetEnumValues(underlying);
            }

            // Array items
            string? itemsType = attr.ItemsType;
            string? itemsFormat = attr.ItemsFormat;
            if (itemsType == null && IsCollectionType(underlying))
            {
                var elementType = GetCollectionElementType(underlying);
                if (elementType != null)
                {
                    itemsType = InferSchemaType(elementType);
                    itemsFormat = attr.ItemsFormat ?? InferSchemaFormat(elementType);
                }
            }

            parameters[snakeName] = new ParameterSchema(
                Type: schemaType,
                Description: attr.Description ?? prop.Name,
                Required: required,
                Default: attr.Default,
                Enum: enumValues,
                Format: format,
                ItemsType: itemsType,
                ItemsFormat: itemsFormat,
                EnumMap: enumMap,
                Example: attr.Example
            );
        }

        return new ToolSchema(name, description, parameters);
    }

    // ── Returns + Types generation ───────────────────────────────────

    /// <summary>
    /// Accumulates discovered non-primitive types during schema generation.
    /// </summary>
    private sealed class SchemaContext
    {
        public Dictionary<string, object?> Types { get; } = [];
        public HashSet<Type> Visited { get; } = [];
    }

    private static (Dictionary<string, object?> Returns, Dictionary<string, object?> Types) BuildReturnsWithTypes(Type responseType)
    {
        var ctx = new SchemaContext();
        var returns = BuildReturnsCore(responseType, ctx);
        return (returns, ctx.Types);
    }

    private static Dictionary<string, object?> BuildReturnsCore(Type responseType, SchemaContext ctx)
    {
        var returns = new Dictionary<string, object?>();
        var props = responseType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // Check if any property has [ReturnSchema] → opt-in mode
        var hasAnyReturnSchema = props.Any(p => p.GetCustomAttribute<ReturnSchemaAttribute>() != null);

        foreach (var prop in props)
        {
            var attr = prop.GetCustomAttribute<ReturnSchemaAttribute>();

            // If no [ReturnSchema] anywhere → auto-infer all properties (convention over configuration)
            // If some [ReturnSchema] exist → only include annotated properties (opt-in mode)
            if (hasAnyReturnSchema && attr == null) continue;

            var snakeName = ToSnakeCase(prop.Name);
            var entry = BuildPropertySchema(prop.PropertyType, attr, ctx);

            if (attr?.Description != null)
                entry["description"] = attr.Description;
            if (attr?.Example != null)
                entry["example"] = attr.Example;

            returns[snakeName] = entry;
        }

        return returns;
    }

    /// <summary>
    /// Builds a JSON Schema-like dictionary for a single property type.
    /// Non-primitive object types are registered in ctx.Types and referenced via "$ref".
    /// </summary>
    private static Dictionary<string, object> BuildPropertySchema(Type propertyType, ReturnSchemaAttribute? attr, SchemaContext ctx)
    {
        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        var schemaType = attr?.Type ?? InferSchemaType(underlying);
        var format = attr?.Format ?? InferSchemaFormat(underlying);

        // Array/collection types → describe items (may produce $ref for object items)
        if (schemaType == "array" || (IsCollectionType(underlying) && attr?.Type != "object"))
        {
            var entry = new Dictionary<string, object> { ["type"] = "array" };
            var elementType = GetCollectionElementType(underlying);
            if (elementType != null)
            {
                entry["items"] = BuildItemSchema(elementType, ctx);
            }
            return entry;
        }

        // Non-primitive object types → extract to types section, emit $ref
        if (schemaType == "object" && IsUserDefinedType(underlying))
        {
            var typeName = ToSnakeCase(underlying.Name);
            RegisterType(underlying, typeName, ctx);
            return new Dictionary<string, object> { ["$ref"] = $"#/types/{typeName}" };
        }

        // Primitive types
        var result = new Dictionary<string, object> { ["type"] = schemaType };
        if (format != null)
            result["format"] = format;
        return result;
    }

    /// <summary>
    /// Builds schema for array/collection element types. Object elements → $ref.
    /// </summary>
    private static Dictionary<string, object> BuildItemSchema(Type elementType, SchemaContext ctx)
    {
        var underlying = Nullable.GetUnderlyingType(elementType) ?? elementType;
        var itemSchemaType = InferSchemaType(underlying);

        // Nested collections (e.g. List<List<string>>)
        if (IsCollectionType(underlying))
        {
            var entry = new Dictionary<string, object> { ["type"] = "array" };
            var nestedElementType = GetCollectionElementType(underlying);
            if (nestedElementType != null)
                entry["items"] = BuildItemSchema(nestedElementType, ctx);
            return entry;
        }

        // Object element → register in types, return $ref
        if (itemSchemaType == "object" && IsUserDefinedType(underlying))
        {
            var typeName = ToSnakeCase(underlying.Name);
            RegisterType(underlying, typeName, ctx);
            return new Dictionary<string, object> { ["$ref"] = $"#/types/{typeName}" };
        }

        // Primitive element
        var items = new Dictionary<string, object> { ["type"] = itemSchemaType };
        var itemFormat = InferSchemaFormat(underlying);
        if (itemFormat != null) items["format"] = itemFormat;
        return items;
    }

    /// <summary>
    /// Registers a non-primitive object type in the types section if not already present.
    /// Recursively describes its properties.
    /// </summary>
    private static void RegisterType(Type objectType, string typeName, SchemaContext ctx)
    {
        // Already registered or cycle guard
        if (ctx.Types.ContainsKey(typeName) || !ctx.Visited.Add(objectType))
            return;

        try
        {
            var properties = new Dictionary<string, object>();

            foreach (var prop in objectType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var snakeName = ToSnakeCase(prop.Name);
                var propSchema = BuildPropertySchema(prop.PropertyType, attr: null, ctx);
                properties[snakeName] = propSchema;
            }

            var typeDef = new Dictionary<string, object> { ["type"] = "object" };
            if (properties.Count > 0)
                typeDef["properties"] = properties;

            ctx.Types[typeName] = typeDef;
        }
        finally
        {
            ctx.Visited.Remove(objectType);
        }
    }

    /// <summary>
    /// Determines if a type is a user-defined class/record (not a BCL primitive, string, collection, etc.).
    /// These types get extracted to the "types" section.
    /// </summary>
    private static bool IsUserDefinedType(Type type)
    {
        if (type.IsPrimitive || type.IsEnum) return false;
        if (type == typeof(string) || type == typeof(decimal) || type == typeof(object)) return false;
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
            type == typeof(DateOnly) || type == typeof(TimeOnly) ||
            type == typeof(TimeSpan) || type == typeof(Guid) || type == typeof(Uri))
            return false;
        if (IsCollectionType(type)) return false;
        // Dictionary<,> is not user-defined for our purposes
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return false;

        return type.IsClass || type.IsValueType; // records, structs, classes
    }

    // ── Collection helpers ───────────────────────────────────────────

    private static bool IsCollectionType(Type type)
    {
        if (type.IsArray) return true;
        if (!type.IsGenericType) return false;
        // Dictionary<,> implements IEnumerable<KeyValuePair<,>> but is semantically an object, not an array
        if (type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return false;

        // Check if it implements IEnumerable<T>
        return type.GetInterfaces()
            .Concat([type])
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
    }

    private static Type? GetCollectionElementType(Type type)
    {
        if (type.IsArray) return type.GetElementType();

        var enumerable = type.GetInterfaces()
            .Concat([type])
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        return enumerable?.GetGenericArguments().FirstOrDefault();
    }

    // ── Naming ───────────────────────────────────────────────────────

    internal static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var result = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0) result.Append('_');
                result.Append(char.ToLowerInvariant(c));
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }
}
