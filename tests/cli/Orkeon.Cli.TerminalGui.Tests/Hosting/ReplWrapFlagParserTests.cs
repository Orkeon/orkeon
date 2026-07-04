using Orkeon.Cli.TerminalGui.Hosting;

namespace Orkeon.Cli.TerminalGui.Tests.Hosting;

public class ReplWrapFlagParserTests
{
    private static readonly string[] ArgsWithoutFlag = ["-c", "config.yaml"];

    [Fact]
    public void Parse_returns_null_when_flag_absent() =>
        Assert.Null(ReplWrapFlagParser.Parse(ArgsWithoutFlag));

    [Theory]
    [InlineData("--repl-wrap", "on", true)]
    [InlineData("--repl-wrap", "off", false)]
    [InlineData("--repl-wrap", "ON", true)]
    [InlineData("--repl-wrap", "OFF", false)]
    [InlineData("--repl-wrap", "true", true)]
    [InlineData("--repl-wrap", "false", false)]
    [InlineData("--repl-wrap", "1", true)]
    [InlineData("--repl-wrap", "0", false)]
    public void Parse_separated_form(string flag, string value, bool expected)
    {
        var result = ReplWrapFlagParser.Parse(new[] { "-c", "x", flag, value });
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("--repl-wrap=on", true)]
    [InlineData("--repl-wrap=off", false)]
    public void Parse_equals_form(string token, bool expected)
    {
        var result = ReplWrapFlagParser.Parse(new[] { "-c", "x", token });
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("yes")]
    [InlineData("")]
    public void Parse_throws_on_unknown_value(string value) =>
        Assert.Throws<ArgumentException>(() => ReplWrapFlagParser.Parse(new[] { "--repl-wrap", value }));

    [Fact]
    public void Parse_throws_on_null_args() =>
        Assert.Throws<ArgumentNullException>(() => ReplWrapFlagParser.Parse(null!));
}
