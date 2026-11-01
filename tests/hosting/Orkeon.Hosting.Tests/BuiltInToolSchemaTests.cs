using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Tools;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// #282 - every argument a typed built-in tool reads is one the model can see. A request property
/// without <c>[FieldSchema]</c> is deserialized but never published (<c>ToolSchemaGenerator</c> keeps
/// the annotated properties alone), so the model received <c>sub_graph</c> without <c>seeds</c> and
/// <c>codebase_search</c> without <c>query</c>. Read on the tools a run registers (the host
/// <c>--list-tools</c> builds), with no allowlist: a gap is fixed on the tool, in any family.
/// Joins <see cref="ConsoleSerialCollection"/> because the host build writes on the console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class BuiltInToolSchemaTests : IDisposable
{
    private readonly string _root;

    public BuiltInToolSchemaTests() => _root = RegisteredTools.CreateRoot("orkeon-tool-schema-");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public async Task ShouldPublishEveryRequestProperty_WhenATypedToolIsRegistered()
    {
        var tools = await RegisteredTools.ReadAsync(_root);
        var gaps = new List<string>();
        var typed = 0;

        foreach (var tool in tools.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            var request = RequestTypeOf(tool);
            if (request is null)
                continue;
            typed++;

            var published = tool.Schema.Parameters?.Keys.ToHashSet(StringComparer.Ordinal) ?? [];
            foreach (var property in ArgumentsOf(request))
            {
                var name = SerializedName(property);
                if (!published.Contains(name))
                    gaps.Add($"{tool.Name}.{name} ({request.Name}.{property.Name})");
            }
        }

        Assert.True(typed > 0, "No typed tool was read from the host.");
        Assert.True(gaps.Count == 0,
            "Request properties the schema does not publish (add [FieldSchema] on each):\n  " + string.Join("\n  ", gaps));
    }

    /// <summary>
    /// The request record of a typed tool: the first parameter of the protected
    /// <c>ExecuteTypedAsync(TRequest, CancellationToken)</c> every <c>ToolBase&lt;,&gt;</c>,
    /// <c>FileToolBase&lt;,&gt;</c> and <c>HttpToolBase&lt;,&gt;</c> family declares; null for a tool that
    /// reads its parameters itself.
    /// </summary>
    private static Type? RequestTypeOf(IBaseTool tool)
    {
        for (var type = tool.GetType(); type is not null; type = type.BaseType)
        {
            var execute = type.GetTypeInfo().DeclaredMethods.FirstOrDefault(m =>
                !m.IsStatic && !m.IsPublic && m.Name == "ExecuteTypedAsync"
                && m.GetParameters() is [var request, var token] && token.ParameterType == typeof(CancellationToken)
                && request.ParameterType.IsClass && request.ParameterType != typeof(string));
            if (execute is not null)
                return execute.GetParameters()[0].ParameterType;
        }

        return null;
    }

    /// <summary>The public properties the deserializer fills from a call: settable, not ignored, not an indexer.</summary>
    private static IEnumerable<PropertyInfo> ArgumentsOf(Type request) =>
        request.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p =>
            p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0
            && p.GetCustomAttribute<JsonIgnoreAttribute>() is not { Condition: JsonIgnoreCondition.Always });

    private static string SerializedName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);
}
