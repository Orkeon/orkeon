using System.Collections.Immutable;
using Jint;
using Jint.Native;

namespace Orkeon.Cli.Scripting.Args;

/// <summary>
/// Parses the <c>args</c> object of a <c>defineCommand({...})</c> descriptor (a Jint
/// <see cref="JsValue"/>) into an ordered, typed list of <see cref="ArgSpec"/>.
/// </summary>
/// <remarks>
/// <para>
/// Property order is preserved. Order matters because positional tokens consume specs in
/// declaration order; we lean on Jint's <c>GetOwnProperties</c> which returns properties
/// in insertion order for plain JS objects.
/// </para>
/// <para>
/// Failures throw <see cref="ArgsSchemaException"/> with a script-friendly message —
/// they bubble out of <see cref="Loading.ScriptCommandLoader.LoadAndRegisterAsync"/> and
/// the script is marked invalid (skipped or fail-fast depending on options).
/// </para>
/// </remarks>
public static class ArgsSchemaParser
{
    /// <summary>Parses the JS <c>args</c> object. Returns an empty list when <paramref name="schemaJs"/> is null/undefined.</summary>
    public static ImmutableArray<ArgSpec> Parse(JsValue? schemaJs)
    {
        if (schemaJs is null || schemaJs.IsUndefined() || schemaJs.IsNull())
            return ImmutableArray<ArgSpec>.Empty;
        if (!schemaJs.IsObject())
            throw new ArgsSchemaException("args must be an object mapping arg names to specs.");

        var obj = schemaJs.AsObject();
        var builder = ImmutableArray.CreateBuilder<ArgSpec>();
        foreach (var key in obj.GetOwnProperties().Select(prop => prop.Key))
        {
            var name = key.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgsSchemaException("arg names must be non-empty strings.");
            var specJs = obj.Get(key);
            if (!specJs.IsObject())
                throw new ArgsSchemaException($"args.{name}: spec must be an object with at least a 'type' field.");
            builder.Add(ParseSingle(name, specJs.AsObject()));
        }
        return builder.ToImmutable();
    }

    private static ArgSpec ParseSingle(string name, Jint.Native.Object.ObjectInstance spec)
    {
        var typeVal = spec.Get("type");
        if (!typeVal.IsString())
            throw new ArgsSchemaException($"args.{name}: 'type' must be one of \"string\" / \"number\" / \"boolean\" / \"string[]\".");
        var required = TryBool(spec.Get("required")) ?? false;

        switch (typeVal.AsString())
        {
            case "string":
            {
                var defaultVal = TryString(spec.Get("default"));
                var choices = ReadStringArray(spec.Get("choices"));
                return new StringArgSpec { Name = name, Required = required, Default = defaultVal, Choices = choices };
            }
            case "number":
            {
                var defaultVal = TryNumber(spec.Get("default"));
                var min = TryNumber(spec.Get("min"));
                var max = TryNumber(spec.Get("max"));
                return new NumberArgSpec { Name = name, Required = required, Default = defaultVal, Min = min, Max = max };
            }
            case "boolean":
            {
                var defaultVal = TryBool(spec.Get("default"));
                return new BooleanArgSpec { Name = name, Required = required, Default = defaultVal };
            }
            case "string[]":
            {
                var defaultJs = spec.Get("default");
                ImmutableArray<string>? def = defaultJs.IsUndefined() || defaultJs.IsNull()
                    ? null
                    : ReadStringArray(defaultJs);
                return new StringArrayArgSpec { Name = name, Required = required, Default = def };
            }
            default:
                throw new ArgsSchemaException($"args.{name}: unsupported type '{typeVal.AsString()}'. Supported: string, number, boolean, string[].");
        }
    }

    private static string? TryString(JsValue v) => v.IsString() ? v.AsString() : null;
    private static double? TryNumber(JsValue v) => v.IsNumber() ? v.AsNumber() : (double?)null;
    private static bool? TryBool(JsValue v) => v.IsBoolean() ? v.AsBoolean() : null;

    private static ImmutableArray<string> ReadStringArray(JsValue v)
    {
        if (v.IsUndefined() || v.IsNull()) return ImmutableArray<string>.Empty;
        if (!v.IsArray()) throw new ArgsSchemaException("expected a string array.");
        var arr = v.AsArray();
        var builder = ImmutableArray.CreateBuilder<string>((int)arr.Length);
        for (uint i = 0; i < arr.Length; i++)
        {
            var elem = arr.Get(i);
            if (!elem.IsString())
                throw new ArgsSchemaException("string-array elements must all be strings.");
            builder.Add(elem.AsString());
        }
        return builder.ToImmutable();
    }
}

/// <summary>Thrown by <see cref="ArgsSchemaParser"/> when a <c>defineCommand args</c> object is malformed.</summary>
[Serializable]
public sealed class ArgsSchemaException : Exception
{
    public ArgsSchemaException(string message) : base(message) { }

    /// <summary>Initializes a new instance.</summary>
    public ArgsSchemaException() { }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    public ArgsSchemaException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Initializes a new instance for deserialization.</summary>
#pragma warning disable SYSLIB0051 // Required by S3925 ISerializable pattern
    private ArgsSchemaException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)
        : base(info, context)
    {
    }
#pragma warning restore SYSLIB0051
}
