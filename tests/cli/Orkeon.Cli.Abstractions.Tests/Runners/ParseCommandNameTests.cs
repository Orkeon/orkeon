using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Tests.Console;
using Orkeon.Cli.Abstractions.Tests.Fixtures;

namespace Orkeon.Cli.Abstractions.Tests.Runners;

public sealed class ParseCommandNameTests : IDisposable
{
    private static readonly string[] XyzArgs = ["xyz"];
    private static readonly string[] JsonVerboseArgs = ["--json", "--verbose"];
    private static readonly string[] XArgs = ["--x"];

    // One adapter per test (xUnit creates a fresh class instance per test method).
    private readonly TestConsoleAdapter _console = new();

    public void Dispose() => _console.Dispose();

    private TestRunner BuildRunner(IEnumerable<RecordingCommand> specific, IEnumerable<RecordingCommand> defaults)
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        return new TestRunner(
            new TestCommandRegistry(defaults.ToArray()),
            new TestCommandRegistry(specific.ToArray()),
            _console,
            sp);
    }

    [Fact]
    public void Single_token_returns_token_and_empty_args()
    {
        var runner = BuildRunner(
            specific: new[] { new RecordingCommand("foo") },
            defaults: Array.Empty<RecordingCommand>());

        var (name, args) = runner.ParseForTest("foo");

        Assert.Equal("foo", name);
        Assert.Empty(args);
    }

    [Fact]
    public void Two_tokens_known_command_returns_compound_name()
    {
        var runner = BuildRunner(
            specific: new[] { new RecordingCommand("agent list") },
            defaults: Array.Empty<RecordingCommand>());

        var (name, args) = runner.ParseForTest("agent list");

        Assert.Equal("agent list", name);
        Assert.Empty(args);
    }

    [Fact]
    public void Two_tokens_unknown_compound_falls_back_to_first_token()
    {
        var runner = BuildRunner(
            specific: new[] { new RecordingCommand("agent") },
            defaults: Array.Empty<RecordingCommand>());

        var (name, args) = runner.ParseForTest("agent xyz");

        Assert.Equal("agent", name);
        Assert.Equal(XyzArgs, args);
    }

    [Fact]
    public void Three_plus_tokens_known_compound_extracts_args()
    {
        var runner = BuildRunner(
            specific: new[] { new RecordingCommand("agent list") },
            defaults: Array.Empty<RecordingCommand>());

        var (name, args) = runner.ParseForTest("agent list --json --verbose");

        Assert.Equal("agent list", name);
        Assert.Equal(JsonVerboseArgs, args);
    }

    [Fact]
    public void Multiple_spaces_collapsed()
    {
        var runner = BuildRunner(
            specific: new[] { new RecordingCommand("agent list") },
            defaults: Array.Empty<RecordingCommand>());

        var (name, args) = runner.ParseForTest("agent   list   --x");

        Assert.Equal("agent list", name);
        Assert.Equal(XArgs, args);
    }

    [Fact]
    public void Case_insensitive_match_for_compound()
    {
        var runner = BuildRunner(
            specific: new[] { new RecordingCommand("agent list") },
            defaults: Array.Empty<RecordingCommand>());

        var (name, args) = runner.ParseForTest("Agent LIST");

        // Parser preserves user casing; resolution matches case-insensitively.
        Assert.Equal("Agent LIST", name);
        Assert.Empty(args);
    }
}
