using System.Collections.Immutable;
using Orkeon.Cli.Commands.Scripting.Args;

namespace Orkeon.Cli.Commands.Scripting.Tests.Args;

public sealed class ArgsTokenParserTests
{
    private static readonly string[] TargetEqualsProd = ["--target=prod"];
    private static readonly string[] TargetSpaceProd = ["--target", "prod"];
    private static readonly string[] VerboseFlag = ["--verbose"];
    private static readonly string[] ProdPositional = ["prod"];
    private static readonly string[] ProdDry = ["prod", "--dry"];
    private static readonly string[] AbcTokens = ["a", "b", "c"];
    private static readonly string[] TagsAbVerbose = ["--tags", "a", "b", "--verbose"];
    private static readonly string[] AbExpected = ["a", "b"];
    private static readonly string[] UnknownX = ["--unknown", "x"];
    private static readonly string[] TargetFlag = ["--target"];
    private static readonly string[] TargetProdAlpha = ["--target", "prod", "alpha"];

    private static StringArgSpec Str(string name, bool required = false) => new() { Name = name, Required = required };
    private static NumberArgSpec Num(string name) => new() { Name = name };
    private static BooleanArgSpec Bool(string name) => new() { Name = name };
    private static StringArrayArgSpec Arr(string name) => new() { Name = name };

    [Fact]
    public void Parses_long_form_with_equals()
    {
        var parsed = ArgsTokenParser.Parse(TargetEqualsProd, new[] { Str("target") });
        Assert.Empty(parsed.Errors);
        Assert.Equal("prod", parsed.Values["target"]);
    }

    [Fact]
    public void Parses_long_form_with_space()
    {
        var parsed = ArgsTokenParser.Parse(TargetSpaceProd, new[] { Str("target") });
        Assert.Empty(parsed.Errors);
        Assert.Equal("prod", parsed.Values["target"]);
    }

    [Fact]
    public void Parses_bare_flag_as_true_for_boolean()
    {
        var parsed = ArgsTokenParser.Parse(VerboseFlag, new[] { Bool("verbose") });
        Assert.Empty(parsed.Errors);
        Assert.Equal(true, parsed.Values["verbose"]);
    }

    [Fact]
    public void Parses_positional()
    {
        var parsed = ArgsTokenParser.Parse(ProdPositional, new[] { Str("target") });
        Assert.Empty(parsed.Errors);
        Assert.Equal("prod", parsed.Values["target"]);
    }

    [Fact]
    public void Parses_mixed_positional_and_flag()
    {
        var schema = new ArgSpec[] { Str("target"), Bool("dry") };
        var parsed = ArgsTokenParser.Parse(ProdDry, schema);
        Assert.Empty(parsed.Errors);
        Assert.Equal("prod", parsed.Values["target"]);
        Assert.Equal(true, parsed.Values["dry"]);
    }

    [Fact]
    public void Greedy_consumes_string_array_positionally()
    {
        var parsed = ArgsTokenParser.Parse(AbcTokens, new[] { Arr("items") });
        Assert.Empty(parsed.Errors);
        var arr = Assert.IsType<ImmutableArray<string>>(parsed.Values["items"]);
        Assert.Equal(AbcTokens, arr);
    }

    [Fact]
    public void Greedy_consumes_string_array_in_flag_form_until_next_dashed()
    {
        var schema = new ArgSpec[] { Arr("tags"), Bool("verbose") };
        var parsed = ArgsTokenParser.Parse(TagsAbVerbose, schema);
        Assert.Empty(parsed.Errors);
        Assert.Equal(AbExpected, (ImmutableArray<string>)parsed.Values["tags"]!);
        Assert.Equal(true, parsed.Values["verbose"]);
    }

    [Fact]
    public void Reports_error_for_unknown_option()
    {
        var parsed = ArgsTokenParser.Parse(UnknownX, new[] { Str("target") });
        Assert.Single(parsed.Errors);
        Assert.Contains("--unknown", parsed.Errors[0]);
    }

    [Fact]
    public void Reports_error_when_named_arg_missing_value()
    {
        var parsed = ArgsTokenParser.Parse(TargetFlag, new[] { Str("target") });
        Assert.Single(parsed.Errors);
        Assert.Contains("--target", parsed.Errors[0]);
    }

    [Fact]
    public void Same_arg_cannot_be_filled_positionally_after_named()
    {
        // After `--target prod`, the positional "alpha" should NOT also fill `target`.
        var schema = new ArgSpec[] { Str("target"), Str("crew") };
        var parsed = ArgsTokenParser.Parse(TargetProdAlpha, schema);
        Assert.Empty(parsed.Errors);
        Assert.Equal("prod", parsed.Values["target"]);
        Assert.Equal("alpha", parsed.Values["crew"]);
    }
}
