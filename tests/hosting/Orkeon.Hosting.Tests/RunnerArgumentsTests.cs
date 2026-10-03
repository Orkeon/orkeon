using CommandLine;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// STUDIO-51: the run grammar reads a single-value option written <c>--option=value</c> — how the
/// team launchers and Studio write one, so a value starting with <c>-</c> stays its value — with
/// the value as written. CommandLineParser 2.9.1 alone refuses an attached value that holds a line
/// break or starts with a space, which left a context of two bullet lines no spelling at all.
/// </summary>
public sealed class RunnerArgumentsTests
{
    public sealed class TestOptions : RunnerOptionsBase;

    private static ParserResult<TestOptions> Parse(params string[] arguments)
    {
        using var parser = new Parser(s => s.HelpWriter = null);
        return RunnerArguments.Parse<TestOptions>(parser, arguments);
    }

    private static TestOptions Parsed(params string[] arguments) =>
        Assert.IsType<Parsed<TestOptions>>(Parse(arguments)).Value;

    /// <summary>
    /// What CommandLineParser reads alone it reads the same; what it refuses alone — a line break,
    /// a leading space — it reads now. Should a later CommandLineParser read those itself, the
    /// stand-in has no reason left to be.
    /// </summary>
    [Theory]
    [InlineData("- puce un\n- puce deux", true)]
    [InlineData("- puce un\r\n- puce deux", true)]
    [InlineData(" commence par une espace", true)]
    [InlineData("- puce", false)]
    [InlineData("a = b", false)]
    public void An_attached_value_is_read_as_written(string context, bool refusedAlone)
    {
        using var stock = new Parser(s => s.HelpWriter = null);
        var alone = stock.ParseArguments<TestOptions>(["--initial-context=" + context]);

        var options = Parsed("--initial-context=" + context, "--settings= C:\\a b\\appsettings.json");

        Assert.Equal(context, options.InitialContext);
        Assert.Equal(" C:\\a b\\appsettings.json", options.SettingsPath);
        Assert.Equal(refusedAlone, alone is NotParsed<TestOptions>);
    }

    [Fact]
    public void Everything_else_reaches_the_parser_as_it_is()
    {
        var options = Parsed("--var", "a=1", "b=x\ny", "--mount", "a:/x:ro", "--initial-context", "Premier essai", "--llm-profile=z-ai");

        Assert.Equal(["a=1", "b=x\ny"], options.Variables);
        Assert.Equal(["a:/x:ro"], options.Mounts);
        Assert.Equal("Premier essai", options.InitialContext);
        Assert.Equal("z-ai", options.LlmProfile);
    }

    /// <summary>What the runner refused stays refused: an empty attached value, a separate value starting with a dash, a repeated option.</summary>
    [Theory]
    [InlineData("BadFormatTokenError", "--initial-context=")]
    [InlineData("UnknownOptionError", "--initial-context", "- puce")]
    [InlineData("RepeatedOptionError", "--initial-context=a\nb", "--initial-context=c\nd")]
    public void What_the_runner_refused_stays_refused(string error, params string[] arguments)
    {
        var refused = Assert.IsType<NotParsed<TestOptions>>(Parse(arguments));

        Assert.Contains(refused.Errors, e => e.Tag.ToString() == error);
    }
}
