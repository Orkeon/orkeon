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
/// the interface <c>OrkeonTools</c> extends (#125). Its input is typed from the request record the tool
/// deserializes - its <c>[JsonPropertyName]</c> names and its required members - or from its
/// <see cref="IBaseTool.Schema"/> when it reads its parameters itself. A parameter renamed or removed on the .NET side changes the declaration, and the
/// test that pins the file fails until it is regenerated. A result is typed from the response record
/// the same way, by the names its serializer writes.
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
            // Written by BuiltInToolTypings from every tool a run registers: the input from the request
            // record of a ToolBase<TReq, TRes> (its JSON names, its required members), else from the
            // tool's schema. Keys are the tool's own snake_case names, as the binding hands the object
            // to the tool. A result is the response record's members the tool hands back, a member the
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
        var fields = (execute is not null ? FieldsOf(execute.GetParameters()[0].ParameterType) : FieldsOf(tool.Schema))
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
              .Append(TypeOf(property.PropertyType)).Append(";\n");
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
            var execute = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == "ExecuteTypedAsync"
                    && m.GetParameters() is [var request, var token]
                    && token.ParameterType == typeof(CancellationToken)
                    && request.ParameterType.IsClass && request.ParameterType != typeof(string));
            if (execute is not null)
                return execute;
        }

        return null;
    }

    /// <summary>
    /// The fields of a request record as the tool deserializes it: the <c>[JsonPropertyName]</c> name,
    /// else the snake_case of the property; required when the member is <c>required</c> or
    /// <c>[JsonRequired]</c> (deserialization refuses the call without it), or when its
    /// <c>[FieldSchema]</c> makes it required (the schema check refuses it first); <c>| null</c> when an
    /// optional member is nullable. A required member never takes null: the schema check lets a null
    /// through as present, and the tool then refuses it (<c>web_scrape</c>: "URL cannot be empty"). A
    /// result needs no <c>| null</c>: the serializer drops a null member.
    /// </summary>
    private static IEnumerable<Field> FieldsOf(Type request)
    {
        foreach (var property in request.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is not { IsPublic: true } || property.GetIndexParameters().Length > 0
                || property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
                continue;

            var schema = property.GetCustomAttribute<FieldSchemaAttribute>();
            var name = SerializedName(property);
            var required = property.IsDefined(typeof(RequiredMemberAttribute), inherit: false)
                || property.IsDefined(typeof(JsonRequiredAttribute), inherit: false)
                || (schema is not null && (schema.Required ?? ToolSchemaGenerator.InferRequired(property)));
            var type = schema?.Enum is { Length: > 0 } choices
                ? string.Join(" | ", choices.Select(c => JsonSerializer.Serialize(c)))
                : TypeOf(property.PropertyType);
            // unknown already takes null.
            if (!required && type != "unknown" && IsNullable(property))
                type += " | null";
            yield return new Field(name, required, type, schema?.Description);
        }
    }

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

    /// <summary>The fields a tool that reads its parameters itself declares in its schema.</summary>
    private static IEnumerable<Field> FieldsOf(ToolSchema schema) =>
        (schema.Parameters ?? []).Select(p => new Field(p.Key, p.Value.Required, TypeOf(p.Value), p.Value.Description));

    /// <summary>The TypeScript type of a CLR property type, by the JSON shape it reads.</summary>
    private static string TypeOf(Type clr)
    {
        var type = Nullable.GetUnderlyingType(clr) ?? clr;
        var schemaType = ToolSchemaGenerator.InferSchemaType(type);
        if (schemaType == "object" && DictionaryValueType(type) is { } values)
            return $"Record<string, {TypeOf(values)}>";
        if (schemaType == "array")
            return ElementType(type) is { } element ? $"readonly {Parenthesize(TypeOf(element))}[]" : "readonly unknown[]";
        return ScalarType(schemaType);
    }

    private static Type? DictionaryValueType(Type type) =>
        (type.IsGenericType ? [type, .. type.GetInterfaces()] : type.GetInterfaces())
            .FirstOrDefault(i => i.IsGenericType
                && i.GetGenericTypeDefinition() is var d
                && (d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>)))
            ?.GetGenericArguments()[1];

    private static Type? ElementType(Type type) =>
        type.IsArray
            ? type.GetElementType()
            : (type.IsGenericType ? [type, .. type.GetInterfaces()] : type.GetInterfaces())
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];

    private static string TypeOf(ParameterSchema schema)
    {
        if (schema.Values is { } values)
            return $"Record<string, {TypeOf(values)}>";
        if (schema.Enum is { Count: > 0 } choices)
            return string.Join(" | ", choices.Select(c => JsonSerializer.Serialize(c)));
        return schema.Type == "array"
            ? $"readonly {Parenthesize(ScalarType(schema.ItemsType))}[]"
            : ScalarType(schema.Type);
    }

    private static string Parenthesize(string type) =>
        type.Contains(' ', StringComparison.Ordinal) && !type.StartsWith("Record<", StringComparison.Ordinal) ? $"({type})" : type;

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
