using Jint;
using Jint.Native;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Builders;

/// <summary>
/// Fluent builder exposed to JS as <c>toolBuilder()</c>. Captures the configuration
/// supplied by the script and produces a <see cref="JsTool"/> on <see cref="build"/>.
/// </summary>
/// <remarks>
/// <c>withSchema</c> accepts two shapes and reads them with full fidelity (EX-01 —
/// the schema is what the LLM sees, losing fields degrades every tool call):
/// <list type="bullet">
/// <item><description>a bare JSON schema — <c>{ type, properties, required }</c>;</description></item>
/// <item><description>the typings' pair — <c>{ input: &lt;schema&gt;, output: &lt;schema&gt; }</c>,
/// where <c>input</c> feeds the parameters and <c>output</c> becomes the tool's
/// return schema.</description></item>
/// </list>
/// Per property: <c>type</c>, <c>description</c>, <c>required</c> (from the schema's
/// list), <c>default</c>, <c>enum</c>, <c>format</c>, <c>items.type</c>/<c>items.format</c>
/// and <c>example</c> all reach <see cref="ParameterSchema"/>.
/// </remarks>
#pragma warning disable IDE1006
#pragma warning disable CS1591
public sealed class JsToolBuilder
{
    private readonly Engine _engine;
    private string? _name;
    private string? _description;
    private string? _access;
    private JsValue? _schema;
    private JsValue? _execute;

    public JsToolBuilder(Engine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public JsToolBuilder name(string value) { _name = value; return this; }
    public JsToolBuilder description(string value) { _description = value; return this; }
    public JsToolBuilder withSchema(JsValue schema) { _schema = schema; return this; }
    public JsToolBuilder execute(JsValue fn) { _execute = fn; return this; }

    /// <summary>
    /// Declared access class for permission gates: "read", "edit" or "execute".
    /// Undeclared stays <see cref="ToolAccess.Unspecified"/>, which gates treat
    /// fail-closed — declare it and an autonomous read-only tool stops being
    /// classified as a write.
    /// </summary>
    public JsToolBuilder access(string value) { _access = value; return this; }

    public JsTool build()
    {
        if (string.IsNullOrWhiteSpace(_name))
            throw new InvalidScriptException("toolBuilder() requires .name(...).");
        if (string.IsNullOrWhiteSpace(_description))
            throw new InvalidScriptException("toolBuilder() requires .description(...).");
        if (_execute is null || _execute.IsUndefined() || _execute.IsNull())
            throw new InvalidScriptException("toolBuilder() requires .execute(...).");

        var hasSchema = _schema is not null && !_schema.IsUndefined() && !_schema.IsNull();
        var schema = hasSchema
            ? BuildSchemaFromJsValue(_name!, _description!, _schema!)
            : new ToolSchema(_name!, _description!, new Dictionary<string, ParameterSchema>());

        return new JsTool(_name!, _description!, schema, hasSchema, _engine, _execute, ParseAccess(_access));
    }

    private static ToolAccess ParseAccess(string? value) => value?.ToUpperInvariant() switch
    {
        null => ToolAccess.Unspecified,
        "READ" => ToolAccess.Read,
        "EDIT" => ToolAccess.Edit,
        "EXECUTE" => ToolAccess.Execute,
        _ => throw new InvalidScriptException(
            $"toolBuilder().access(...) accepts 'read', 'edit' or 'execute' (got '{value}')."),
    };

    private static ToolSchema BuildSchemaFromJsValue(string name, string description, JsValue schema)
    {
        var parameters = new Dictionary<string, ParameterSchema>();
        Dictionary<string, object?>? returns = null;
        if (schema.IsObject())
        {
            // {input, output} (the typings' shape) or a bare JSON schema.
            var input = schema.Get("input");
            var output = schema.Get("output");
            var effective = input.IsObject() ? input : schema;

            var requiredSet = ReadRequiredSet(effective.Get("required"));
            PopulateParameters(effective.Get("properties"), requiredSet, parameters);

            if (output.IsObject())
                returns = ToPlainDictionary(output);
        }
        return new ToolSchema(name, description, parameters, returns);
    }

    private static HashSet<string> ReadRequiredSet(JsValue required)
    {
        var requiredSet = new HashSet<string>(StringComparer.Ordinal);
        if (required is Jint.Native.Array.ArrayInstance arr)
        {
            var len = (uint)Jint.Runtime.TypeConverter.ToInteger(arr.Get("length"));
            for (uint i = 0; i < len; i++)
            {
                var v = arr.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (v.IsString()) requiredSet.Add(v.AsString());
            }
        }
        return requiredSet;
    }

    private static void PopulateParameters(
        JsValue props,
        HashSet<string> requiredSet,
        Dictionary<string, ParameterSchema> parameters)
    {
        if (!props.IsObject())
            return;

        foreach (var prop in props.AsObject().GetOwnProperties())
        {
            var key = prop.Key.ToString()!;
            var spec = prop.Value.Value;
            if (!spec.IsObject())
            {
                parameters[key] = new ParameterSchema("string", "", requiredSet.Contains(key));
                continue;
            }

            string? itemsType = null;
            string? itemsFormat = null;
            var items = spec.Get("items");
            if (items.IsObject())
            {
                itemsType = GetString(items, "type");
                itemsFormat = GetString(items, "format");
            }

            var enumValues = ReadEnumValues(spec.Get("enum"));

            parameters[key] = new ParameterSchema(
                GetString(spec, "type") ?? "string",
                GetString(spec, "description") ?? "",
                requiredSet.Contains(key),
                Default: GetObject(spec, "default"),
                Enum: enumValues.Count > 0 ? enumValues : null,
                Format: GetString(spec, "format"),
                ItemsType: itemsType,
                ItemsFormat: itemsFormat,
                Example: GetObject(spec, "example"));
        }
    }

    private static string? GetString(JsValue obj, string key)
    {
        var v = obj.Get(key);
        return v.IsString() ? v.AsString() : null;
    }

    private static object? GetObject(JsValue obj, string key)
    {
        var v = obj.Get(key);
        return v.IsUndefined() || v.IsNull() ? null : v.ToObject();
    }

    /// <summary>
    /// Reads a property's <c>enum</c> constraint. Returns an EMPTY list when the schema
    /// declares none (or declares something that is not an array); the caller maps that
    /// to a null <c>ParameterSchema.Enum</c>, which is how "no constraint" is spelled
    /// in the schema record.
    /// </summary>
    private static List<object> ReadEnumValues(JsValue value)
    {
        var values = new List<object>();
        if (value is not Jint.Native.Array.ArrayInstance arr)
            return values;

        var len = (uint)Jint.Runtime.TypeConverter.ToInteger(arr.Get("length"));
        for (uint i = 0; i < len; i++)
        {
            var v = arr.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToObject();
            if (v is not null) values.Add(v);
        }
        return values;
    }

    private static Dictionary<string, object?> ToPlainDictionary(JsValue obj)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in obj.AsObject().GetOwnProperties())
            dict[prop.Key.ToString()!] = prop.Value.Value.ToObject();
        return dict;
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
