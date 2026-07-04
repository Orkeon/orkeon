using Orkeon.Cli.TerminalGui.Hosting;

namespace Orkeon.Cli.TerminalGui.Tests.Hosting;

public class UiFlagParserTests
{
    private static readonly string[] ArgsWithoutFlag = ["-c", "config.yaml"];

    [Fact]
    public void Parse_returns_null_when_flag_absent() =>
        Assert.Null(UiFlagParser.Parse(ArgsWithoutFlag));

    [Theory]
    [InlineData("--ui", "tui", UiMode.Tui)]
    [InlineData("--ui", "plain", UiMode.Plain)]
    [InlineData("--ui", "TUI", UiMode.Tui)]
    [InlineData("--ui", "PLAIN", UiMode.Plain)]
    public void Parse_separated_form(string flag, string value, UiMode expected)
    {
        var result = UiFlagParser.Parse(new[] { "-c", "x", flag, value });
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("--ui=tui", UiMode.Tui)]
    [InlineData("--ui=plain", UiMode.Plain)]
    public void Parse_equals_form(string token, UiMode expected)
    {
        var result = UiFlagParser.Parse(new[] { "-c", "x", token });
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("AUTO")]
    [InlineData("")]
    public void Parse_returns_null_for_auto(string value)
    {
        var result = UiFlagParser.Parse(new[] { "--ui", value });
        Assert.Null(result);
    }

    [Fact]
    public void ParseValue_throws_for_unknown_value()
    {
        Assert.Throws<ArgumentException>(() => UiFlagParser.ParseValue("graphical"));
    }
}
