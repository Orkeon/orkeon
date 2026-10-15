using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Application.Tests.Interfaces.Security;

/// <summary>
/// Which calls a <see cref="ToolPermissionRule"/> covers: its tool, without regard to case, and, when it names an
/// argument, the calls whose argument matches its pattern whole, <c>*</c> standing for any run of characters.
/// </summary>
public sealed class ToolPermissionRuleTests
{
    private static readonly Dictionary<string, object?> NoArguments = new();

    private static ToolPermissionRule Rule(string tool = "shell_command", string? argument = null, string? pattern = null)
        => new() { Effect = ToolPermissionEffect.Allow, ToolName = tool, Argument = argument, Pattern = pattern };

    private static Dictionary<string, object?> Command(object? value) => new() { ["command"] = value };

    [Fact]
    public void ShouldCoverEveryCallOfItsTool_WhenItNamesNoArgument()
    {
        Assert.True(Rule().Matches("shell_command", Command("anything")));
        Assert.True(Rule().Matches("SHELL_COMMAND", NoArguments));
        Assert.False(Rule().Matches("file_write", NoArguments));
    }

    [Theory]
    [InlineData("npm test*", "npm test", true)]
    [InlineData("npm test*", "npm test --watch", true)]
    [InlineData("npm test*", "npm run test", false)]
    [InlineData("*test*", "npm run test:unit", true)]
    [InlineData("git log", "git log", true)]
    [InlineData("git log", "git log -1", false)]
    [InlineData("a*b*c", "aXbYbZc", true)]
    [InlineData("a*b*c", "aXbYbZ", false)]
    [InlineData("NPM*", "npm test", false)]
    [InlineData("*", "", true)]
    public void ShouldMatchTheArgumentWhole_WithStarForAnyRun(string pattern, string value, bool expected)
    {
        Assert.Equal(expected, Rule(argument: "command", pattern: pattern).Matches("shell_command", Command(value)));
    }

    [Fact]
    public void ShouldNotCoverACall_WhenTheArgumentItNamesIsAbsentOrNull()
    {
        var rule = Rule(argument: "command", pattern: "*");

        Assert.False(rule.Matches("shell_command", NoArguments));
        Assert.False(rule.Matches("shell_command", Command(null)));
    }

    [Fact]
    public void ShouldCoverAnyValue_WhenItNamesAnArgumentWithoutPattern()
    {
        Assert.True(Rule(argument: "command").Matches("shell_command", Command("ls")));
    }

    [Fact]
    public void ShouldReadAValueThatIsNotAString_ByItsText()
    {
        var rule = Rule(tool: "http_api", argument: "port", pattern: "80*");

        Assert.True(rule.Matches("http_api", new Dictionary<string, object?> { ["port"] = 8080 }));
    }

    [Fact]
    public void ShouldReadAsOneLine()
    {
        Assert.Equal("allow shell_command", Rule().ToString());
        Assert.Equal("allow shell_command command=npm test*", Rule(argument: "command", pattern: "npm test*").ToString());
        Assert.Equal(
            "deny file_write path=*",
            new ToolPermissionRule { Effect = ToolPermissionEffect.Deny, ToolName = "file_write", Argument = "path" }.ToString());
    }
}
