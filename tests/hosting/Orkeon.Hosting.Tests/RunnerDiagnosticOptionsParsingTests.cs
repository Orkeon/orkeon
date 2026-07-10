using CommandLine;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// Parsing contract for the two diagnostic flags added to <see cref="RunnerOptionsBase"/>:
/// <c>--validate</c> (dry-run crew load) and <c>--list-tools</c> (runtime tool manifest).
/// Notably asserts that <c>--config</c> is no longer parser-required, so <c>--list-tools</c>
/// runs on its own.
/// </summary>
public class RunnerDiagnosticOptionsParsingTests
{
    public sealed class TestOptions : RunnerOptionsBase { }

    private static TestOptions Parse(params string[] args)
    {
        TestOptions? parsed = null;
        Parser.Default.ParseArguments<TestOptions>(args).WithParsed(o => parsed = o);
        Assert.NotNull(parsed);
        return parsed!;
    }

    [Fact]
    public void Validate_flag_parses_with_config()
    {
        var opts = Parse("--config", "crew.yaml", "--validate");
        Assert.True(opts.Validate);
        Assert.False(opts.ListTools);
        Assert.Equal("crew.yaml", opts.ConfigPath);
    }

    [Fact]
    public void ListTools_flag_parses_without_config()
    {
        var opts = Parse("--list-tools");
        Assert.True(opts.ListTools);
        Assert.False(opts.Validate);
        Assert.True(string.IsNullOrEmpty(opts.ConfigPath));
    }

    [Fact]
    public void Both_flags_default_to_false()
    {
        var opts = Parse("--config", "crew.yaml");
        Assert.False(opts.Validate);
        Assert.False(opts.ListTools);
    }

    [Fact]
    public void Config_alone_still_parses_after_becoming_optional()
    {
        // Sanity: relaxing Required on --config must not break the normal path.
        var opts = Parse("-c", "/abs/crew.yaml");
        Assert.Equal("/abs/crew.yaml", opts.ConfigPath);
    }
}
