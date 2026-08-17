using System.Collections.Immutable;
using Orkeon.Cli.Commands.Scripting.Args;

namespace Orkeon.Cli.Commands.Scripting.Tests.Args;

public sealed class ArgsValidatorTests
{
    private static readonly string[] TargetStaging = ["--target=staging"];
    private static readonly string[] TargetProd = ["--target=prod"];
    private static readonly string[] NumFive = ["--n=5"];
    private static readonly string[] NumZero = ["--n=0"];
    private static readonly string[] NumNinetyNine = ["--n=99"];
    private static readonly string[] NumAbc = ["--n=abc"];
    private static readonly string[] VerboseTrue = ["--verbose=true"];
    private static readonly string[] VerboseFalse = ["--verbose=false"];
    private static readonly string[] XyExpected = ["x", "y"];

    private static ArgsValidator.Result Validate(string[] tokens, params ArgSpec[] schema)
        => ArgsValidator.Validate(ArgsTokenParser.Parse(tokens, schema), schema);

    [Fact]
    public void Required_arg_missing_yields_error()
    {
        var result = Validate(Array.Empty<string>(),
            new StringArgSpec { Name = "target", Required = true });
        Assert.False(result.IsSuccess);
        Assert.Contains("--target", result.Errors[0]);
    }

    [Fact]
    public void Default_used_when_not_provided()
    {
        var result = Validate(Array.Empty<string>(),
            new StringArgSpec { Name = "target", Default = "dev" });
        Assert.True(result.IsSuccess);
        Assert.Equal("dev", result.Values!["target"]);
    }

    [Fact]
    public void Choices_rejects_out_of_range_value()
    {
        var result = Validate(TargetStaging,
            new StringArgSpec { Name = "target", Choices = ImmutableArray.Create("dev", "prod") });
        Assert.False(result.IsSuccess);
        Assert.Contains("choices", result.Errors[0]);
    }

    [Fact]
    public void Choices_accepts_in_range_value()
    {
        var result = Validate(TargetProd,
            new StringArgSpec { Name = "target", Choices = ImmutableArray.Create("dev", "prod") });
        Assert.True(result.IsSuccess);
        Assert.Equal("prod", result.Values!["target"]);
    }

    [Fact]
    public void Number_parses_and_enforces_min_max()
    {
        var ok = Validate(NumFive, new NumberArgSpec { Name = "n", Min = 1, Max = 10 });
        Assert.True(ok.IsSuccess);
        Assert.Equal(5.0, ok.Values!["n"]);

        var tooLow = Validate(NumZero, new NumberArgSpec { Name = "n", Min = 1, Max = 10 });
        Assert.False(tooLow.IsSuccess);
        Assert.Contains("below min", tooLow.Errors[0]);

        var tooHigh = Validate(NumNinetyNine, new NumberArgSpec { Name = "n", Min = 1, Max = 10 });
        Assert.False(tooHigh.IsSuccess);
        Assert.Contains("above max", tooHigh.Errors[0]);
    }

    [Fact]
    public void Number_fails_on_non_numeric_input()
    {
        var result = Validate(NumAbc, new NumberArgSpec { Name = "n" });
        Assert.False(result.IsSuccess);
        Assert.Contains("cannot parse", result.Errors[0]);
    }

    [Fact]
    public void Boolean_parses_explicit_value()
    {
        var t = Validate(VerboseTrue, new BooleanArgSpec { Name = "verbose" });
        Assert.True(t.IsSuccess);
        Assert.Equal(true, t.Values!["verbose"]);

        var f = Validate(VerboseFalse, new BooleanArgSpec { Name = "verbose" });
        Assert.True(f.IsSuccess);
        Assert.Equal(false, f.Values!["verbose"]);
    }

    [Fact]
    public void String_array_default_kicks_in()
    {
        var result = Validate(Array.Empty<string>(),
            new StringArrayArgSpec { Name = "tags", Default = ImmutableArray.Create("x", "y") });
        Assert.True(result.IsSuccess);
        var arr = Assert.IsType<ImmutableArray<string>>(result.Values!["tags"]);
        Assert.Equal(XyExpected, arr);
    }
}
