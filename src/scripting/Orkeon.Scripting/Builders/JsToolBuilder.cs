using Jint;
using Jint.Native;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Builders;

/// <summary>
/// Fluent builder exposed to JS as <c>toolBuilder()</c>. Captures the configuration
/// supplied by the script and produces a <see cref="JsTool"/> on <see cref="build"/>.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591
public sealed class JsToolBuilder
{
    private readonly Engine _engine;
    private string? _name;
    private string? _description;
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

        return new JsTool(_name!, _description!, schema, hasSchema, _engine, _execute);
    }

    private static ToolSchema BuildSchemaFromJsValue(string name, string description, JsValue schema)
    {
        var parameters = new Dictionary<string, ParameterSchema>();
        if (schema.IsObject())
        {
            var requiredSet = ReadRequiredSet(schema.Get("required"));
            PopulateParameters(schema.Get("properties"), requiredSet, parameters);
        }
        return new ToolSchema(name, description, parameters);
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
            var type = spec.IsObject() && spec.Get("type").IsString() ? spec.Get("type").AsString() : "string";
            var desc = spec.IsObject() && spec.Get("description").IsString() ? spec.Get("description").AsString() : "";
            parameters[key] = new ParameterSchema(type, desc, requiredSet.Contains(key));
        }
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
