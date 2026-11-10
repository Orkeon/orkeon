using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Writes <c>Typings/tools.generated.d.ts</c>: one member per built-in tool in <c>OrkeonBuiltInTools</c>,
/// the interface <c>OrkeonTools</c> extends (#125). Its input is typed from the tool's published
/// <see cref="IBaseTool.Schema"/> - the parameters, their required flag, types, enums and descriptions
/// the model reads (#282): a request property without <c>[FieldSchema]</c> is absent here as it is
/// for the model - with <c>| null</c> read on the request record for an optional nullable member. A
/// parameter renamed or removed on the .NET side changes the declaration, and the test that pins
/// the file fails until it is regenerated. A result is typed from the response record, by the names
/// its serializer writes.
/// </summary>
/// <remarks>
/// The e-mail and drive families keep their hand-written declarations in <c>tools.d.ts</c>: their own
/// tests run the real tools through them.
/// </remarks>
internal static partial class BuiltInToolTypings
{
    /// <summary>The file this writes, beside the other typings of the package.</summary>
    internal const string FileName = "tools.generated.d.ts";

    /// <summary>The prefixes of the families whose declarations stay hand-written in <c>tools.d.ts</c>.</summary>
    internal static readonly IReadOnlyList<string> HandWrittenFamilies = ["email_", "drive_"];

    /// <summary>The declaration file for <paramref name="tools"/>, one member per tool name, ordered by name.</summary>
    /// <param name="tools">The tools a run registers.</param>
    internal static string Render(IEnumerable<IBaseTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var sb = new StringBuilder();
        sb.Append("""
            // Orkeon Scripting DSL - built-in tools, GENERATED: do not edit (#125).
            // Written by BuiltInToolTypings from every tool a run registers: the input from the schema
            // the tool publishes (its parameters, required flags, types, enums and descriptions), the
            // same the model reads; `| null` where the request record takes a null. Keys are the tool's
            // own snake_case names, as the binding hands the object to the tool. A result is the response record's members the tool hands back, a member the
            // serializer drops when null optional; `unknown` for a tool without a response record. The
            // e-mail and drive families are declared by hand in tools.d.ts.
            // Regenerate after a tool change:
            //   ORKEON_REGENERATE_TYPINGS=1 dotnet test tests/hosting/Orkeon.Hosting.Tests/Orkeon.Hosting.Tests.csproj --no-build --filter "FullyQualifiedName~BuiltInToolTypingsTests"

            declare global {
                /** The built-in tools, by their camelCase name: `tools` (`OrkeonTools`) extends this. */
                interface OrkeonBuiltInTools {

            """);

        var members = tools
            .Where(t => !HandWrittenFamilies.Any(prefix => t.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .DistinctBy(t => t.Name, StringComparer.Ordinal)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
        for (var i = 0; i < members.Count; i++)
        {
            if (i > 0)
                sb.Append('\n');
            AppendMember(sb, members[i]);
        }

        sb.Append("""
                }
            }

            export { };

            """);
        return sb.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>One input field: its JSON name, whether a call must pass it, its TypeScript type, its description.</summary>
    private sealed record Field(string Name, bool Required, string Type, string? Description);

    private static void AppendMember(StringBuilder sb, IBaseTool tool)
    {
        var execute = TypedExecute(tool);
        var fields = FieldsOf(tool.Schema, execute?.GetParameters()[0].ParameterType)
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();
        var optional = fields.All(f => !f.Required);

        AppendDoc(sb, "        ", $"`{tool.Name}` - {tool.Description}");
        sb.Append("        ").Append(ToolsNamespaceBinding.ToCamelCase(tool.Name))
          .Append(optional ? "(input?: " : "(input: ");
        if (fields.Count == 0)
        {
            sb.Append("Record<string, never>");
        }
        else
        {
            sb.Append("{\n");
            foreach (var field in fields)
            {
                AppendDoc(sb, "            ", field.Description);
                sb.Append("            ").Append(PropertyName(field.Name)).Append(field.Required ? ": " : "?: ")
                  .Append(field.Type).Append(";\n");
            }
            sb.Append("        }");
        }
        sb.Append(", ctx?: ExecutionContext): Promise<");
        AppendResult(sb, tool, execute);
        sb.Append(">;\n");
    }

    /// <summary>
    /// The result of a typed tool: the members of its response record the tool hands back - the
    /// serialized name of each, kept when the schema's returns declare it (the tool filters its
    /// output to them) - optional where the serializer drops a null; <c>unknown</c> otherwise.
    /// </summary>
    private static void AppendResult(StringBuilder sb, IBaseTool tool, MethodInfo? execute)
    {
        var response = execute?.ReturnType is { IsGenericType: true } task && task.GetGenericTypeDefinition() == typeof(Task<>)
            ? task.GetGenericArguments()[0]
            : null;
        if (response is null || !response.IsClass || response == typeof(string))
        {
            sb.Append("unknown");
            return;
        }

        var declared = tool.Schema.Returns is { Count: > 0 } returns ? returns.Keys.ToHashSet(StringComparer.Ordinal) : null;
        var members = response.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0
                && p.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always })
            .Select(p => (Property: p, Name: SerializedName(p)))
            .Where(m => declared is null || declared.Contains(m.Name))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
        if (members.Count == 0)
        {
            sb.Append("unknown");
            return;
        }

        sb.Append("{\n");
        foreach (var (property, name) in members)
        {
            sb.Append("            readonly ").Append(PropertyName(name))
              .Append(ToolSchemaGenerator.InferRequired(property) ? ": " : "?: ")
              // A result is read, never built by the script: a nested record shows its members (#226).
              .Append(TypeOf(property.PropertyType, expand: true, [])).Append(";\n");
        }
        sb.Append("        }");
    }

    private static string SerializedName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);

    /// <summary>
    /// The <c>ExecuteTypedAsync(TReq, CancellationToken)</c> of a typed tool, which every
    /// <c>ToolBase&lt;TReq, TRes&gt;</c> family implements: its parameter is the request record, its
    /// result the response record. Null for a tool that reads its parameters itself.
    /// </summary>
    private static MethodInfo? TypedExecute(IBaseTool tool)
    {
        for (var type = tool.GetType(); type is not null; type = type.BaseType)
        {
            // The methods the type declares itself, as the reflection metadata lists them: the
            // protected (non-public) instance ExecuteTypedAsync is the one a typed family declares.
            var execute = type.GetTypeInfo().DeclaredMethods
                .FirstOrDefault(m => !m.IsStatic && !m.IsPublic && m.Name == "ExecuteTypedAsync"
                    && m.GetParameters() is [var request, var token]
                    && token.ParameterType == typeof(CancellationToken)
                    && request.ParameterType.IsClass && request.ParameterType != typeof(string));
            if (execute is not null)
                return execute;
        }

        return null;
    }

    /// <summary>
    /// The fields of a tool's input, read from its published schema: the parameter's name, whether a
    /// call must pass it, its type (an enum or enum map as the literal union, an array of records by
    /// their item properties) and its description, exactly as the model reads them. <c>| null</c> is
    /// added for an optional parameter whose member of <paramref name="request"/> is nullable
    /// (<c>SessionStoreRequest.Value</c>: null removes the key); a required member never takes null.
    /// </summary>
    private static IEnumerable<Field> FieldsOf(ToolSchema schema, Type? request)
    {
        var nullable = request is null ? null : NullableNamesOf(request);
        foreach (var (name, parameter) in schema.Parameters ?? [])
        {
            var type = TypeOf(parameter);
            if (!parameter.Required && type != "unknown" && nullable is not null && nullable.Contains(name))
                type += " | null";
            yield return new Field(name, parameter.Required, type, parameter.Description);
        }
    }

    /// <summary>The serialized names of the nullable members of a request record.</summary>
    private static HashSet<string> NullableNamesOf(Type request) =>
        request.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0 && IsNullable(p))
            .Select(SerializedName)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether the member takes a JSON <c>null</c>: a <see cref="Nullable{T}"/>, or a reference type
    /// annotated nullable (<c>SessionStoreRequest.Value</c>: null removes the key). Read on the
    /// property itself, never on its elements.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Reflection fault barrier, as ToolSchemaGenerator.InferRequired: NullabilityInfoContext.Create can throw on edge-case metadata; the member is then rendered without null rather than aborting the rendering.")]
    private static bool IsNullable(PropertyInfo property)
    {
        if (property.PropertyType.IsValueType)
            return Nullable.GetUnderlyingType(property.PropertyType) is not null;

        try
        {
            // Thread-static, as in ToolSchemaGenerator.InferRequired: NullabilityInfoContext is not thread-safe.
            var context = t_nullability ??= new NullabilityInfoContext();
            return context.Create(property).WriteState == NullabilityState.Nullable;
        }
        catch
        {
            return false;
        }
    }

    [ThreadStatic]
    private static NullabilityInfoContext? t_nullability;

    /// <summary>
    /// <paramref name="expand"/> renders a record type by its members - the elements of an array, a member
    /// of a tool's result, and whatever those members hold - where a bare input property keeps
    /// <c>Record&lt;string, unknown&gt;</c>.
    /// <paramref name="path"/> holds the records being rendered, a cycle falls back to that default;
    /// <see cref="MaxRecordDepth"/> bounds the nesting.
    /// </summary>
    private static string TypeOf(Type clr, bool expand, HashSet<Type> path)
    {
        var type = Nullable.GetUnderlyingType(clr) ?? clr;
        var schemaType = ToolSchemaGenerator.InferSchemaType(type);
        if (schemaType == "object" && DictionaryValueType(type) is { } values)
            return $"Record<string, {TypeOf(values, expand, path)}>";
        if (schemaType == "array")
            return ElementType(type) is { } element
                ? $"readonly {Parenthesize(TypeOf(element, expand: true, path))}[]"
                : "readonly unknown[]";
        if (expand && schemaType == "object" && type.IsClass && path.Count < MaxRecordDepth && path.Add(type))
        {
            try
            {
                if (RecordType(type, path) is { } record)
                    return record;
            }
            finally
            {
                path.Remove(type);
            }
        }
        return ScalarType(schemaType);
    }

    /// <summary>
    /// The TypeScript type of a published parameter: a dictionary by its values, an enum map (a C# enum)
    /// or a flat enum as the literal union of its names, an array of records by their item properties
    /// (#210), else the scalar of its JSON type.
    /// </summary>
    private static string TypeOf(ParameterSchema schema)
    {
        if (schema.Values is { } values)
            return $"Record<string, {TypeOf(values)}>";
        if (schema.EnumMap is { Count: > 0 } names)
            return string.Join(" | ", names.Keys.Select(n => JsonSerializer.Serialize(n)));
        if (schema.Enum is { Count: > 0 } choices)
            return string.Join(" | ", choices.Select(c => JsonSerializer.Serialize(c)));
        if (schema.Type == "array")
        {
            var item = schema.ItemProperties is { Count: > 0 } properties
                ? "{ " + string.Join(" ", properties
                    .OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => $"readonly {PropertyName(p.Key)}{(p.Value.Required ? "" : "?")}: {TypeOf(p.Value)};")) + " }"
                : ScalarType(schema.ItemsType);
            return $"readonly {Parenthesize(item)}[]";
        }

        return ScalarType(schema.Type);
    }

    /// <summary>A record type by its members, or null when it has none.</summary>
    private static string? RecordType(Type type, HashSet<Type> path)
    {
        var members = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0
                && p.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always })
            .Select(p => (Property: p, Name: SerializedName(p)))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
        if (members.Count == 0)
            return null;

        // A member's [FieldSchema] rules as at the top level (#210): its Enum is the literal union,
        // its IsRequired the optionality; the schema the model reads and the typings say the same.
        return "{ " + string.Join(" ", members.Select(m =>
        {
            var schema = m.Property.GetCustomAttribute<FieldSchemaAttribute>();
            var required = schema?.Required ?? ToolSchemaGenerator.InferRequired(m.Property);
            var memberType = schema?.Enum is { Length: > 0 } choices
                ? string.Join(" | ", choices.Select(c => JsonSerializer.Serialize(c)))
                : TypeOf(m.Property.PropertyType, expand: true, path);
            return $"readonly {PropertyName(m.Name)}{(required ? "" : "?")}: {memberType};";
        })) + " }";
    }

    private const int MaxRecordDepth = 4;

    private static Type? DictionaryValueType(Type type) =>
        (type.IsGenericType ? [type, .. type.GetInterfaces()] : type.GetInterfaces())
            .FirstOrDefault(i => i.IsGenericType
                && i.GetGenericTypeDefinition() is var d
                && (d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>)))
            ?.GetGenericArguments()[1];

    private static Type? ElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        Type[] candidates = type.IsGenericType ? [type, .. type.GetInterfaces()] : type.GetInterfaces();
        return candidates
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static string Parenthesize(string type) =>
        type.Contains(' ', StringComparison.Ordinal) && !type.StartsWith("Record<", StringComparison.Ordinal) && !type.StartsWith('{') ? $"({type})" : type;

    private static string ScalarType(string? type) => type switch
    {
        "string" => "string",
        "integer" or "number" => "number",
        "boolean" => "boolean",
        "object" => "Record<string, unknown>",
        "array" => "readonly unknown[]",
        _ => "unknown",
    };

    private static string PropertyName(string name) =>
        IdentifierPattern().IsMatch(name) ? name : JsonSerializer.Serialize(name);

    private static void AppendDoc(StringBuilder sb, string indent, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        var flat = WhitespacePattern().Replace(text.Trim(), " ").Replace("*/", "*\\/", StringComparison.Ordinal);
        sb.Append(indent).Append("/** ").Append(flat).Append(" */\n");
    }

    [GeneratedRegex(@"^[A-Za-z_$][A-Za-z0-9_$]*$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
