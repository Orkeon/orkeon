using Jint;
using Jint.Native;
using Orkeon.Cli.Commands.Scripting.Args;

namespace Orkeon.Cli.Commands.Scripting.Tests.Args;

public sealed class ArgsSchemaParserTests : IDisposable
{
    private static readonly string[] DevProdChoices = ["dev", "prod"];
    private static readonly string[] AbArray = ["a", "b"];
    private static readonly string[] DeclarationOrder = ["target", "count", "flag"];

    private readonly Engine _engine = new();

    public void Dispose() => _engine.Dispose();

    private JsValue Parse(string js) => _engine.Evaluate($"({js})");

    [Fact]
    public void Empty_schema_when_null_or_undefined()
    {
        Assert.True(ArgsSchemaParser.Parse(JsValue.Undefined).IsDefaultOrEmpty);
        Assert.True(ArgsSchemaParser.Parse(JsValue.Null).IsDefaultOrEmpty);
        Assert.Empty(ArgsSchemaParser.Parse(null));
    }

    [Fact]
    public void Parses_string_arg_with_choices_and_default()
    {
        var schema = ArgsSchemaParser.Parse(Parse("""
            { target: { type: "string", required: true, choices: ["dev","prod"], default: "dev" } }
        """));
        var s = Assert.Single(schema);
        var ss = Assert.IsType<StringArgSpec>(s);
        Assert.Equal("target", ss.Name);
        Assert.True(ss.Required);
        Assert.Equal("dev", ss.Default);
        Assert.Equal(DevProdChoices, ss.Choices);
    }

    [Fact]
    public void Parses_number_arg_with_min_max_default()
    {
        var schema = ArgsSchemaParser.Parse(Parse("""
            { count: { type: "number", min: 1, max: 10, default: 3 } }
        """));
        var ns = Assert.IsType<NumberArgSpec>(Assert.Single(schema));
        Assert.Equal(1, ns.Min);
        Assert.Equal(10, ns.Max);
        Assert.Equal(3, ns.Default);
    }

    [Fact]
    public void Parses_boolean_arg_with_default()
    {
        var schema = ArgsSchemaParser.Parse(Parse("""{ verbose: { type: "boolean", default: true } }"""));
        var bs = Assert.IsType<BooleanArgSpec>(Assert.Single(schema));
        Assert.True(bs.Default);
    }

    [Fact]
    public void Parses_string_array_arg_with_default()
    {
        var schema = ArgsSchemaParser.Parse(Parse("""{ tags: { type: "string[]", default: ["a","b"] } }"""));
        var arr = Assert.IsType<StringArrayArgSpec>(Assert.Single(schema));
        Assert.Equal(AbArray, arr.Default!.Value);
    }

    [Fact]
    public void Preserves_declaration_order()
    {
        var schema = ArgsSchemaParser.Parse(Parse("""
            {
              target: { type: "string" },
              count:  { type: "number" },
              flag:   { type: "boolean" }
            }
        """));
        Assert.Equal(DeclarationOrder, schema.Select(s => s.Name));
    }

    [Theory]
    [InlineData("""{ x: { type: "unsupported" } }""")]
    [InlineData("""{ x: { } }""")]              // no type
    [InlineData("""{ x: "string" }""")]         // wrong spec shape
    public void Rejects_invalid_specs(string js)
    {
        Assert.Throws<ArgsSchemaException>(() => ArgsSchemaParser.Parse(Parse(js)));
    }
}
