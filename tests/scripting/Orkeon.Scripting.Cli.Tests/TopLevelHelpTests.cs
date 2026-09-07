using System.Reflection;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// What the bare <c>orkeon</c> binary answers before any verb is chosen: the usage listing,
/// the version, and the refusal of a first token that means nothing (D3-05).
/// <para>
/// The entry point used to hand everything it did not recognise to the <c>run</c> parser, so
/// <c>orkeon --help</c> printed the run option table — never naming <c>init</c>, the verb
/// every quickstart opens with — and exited 1 while doing it. These assertions pin the
/// discovery path a newcomer actually walks.
/// </para>
/// </summary>
[Collection(CliCollection.Name)]
public sealed class TopLevelHelpTests
{
    /// <summary>Every verb the usage must name, whatever the wording around it.</summary>
    private static readonly string[] ExpectedVerbs = ["run", "init", "doctor", "llm", "rag", "forge"];

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public async Task Help_ListsEveryVerb_AndExitsZero(string token)
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync([token]);

        Assert.Equal(Program.ExitOk, exit);
        foreach (var verb in ExpectedVerbs)
            Assert.Contains(verb, console.Stdout, StringComparison.Ordinal);
    }

    /// <summary>Typing the bare tool name is the same question as asking for help.</summary>
    [Fact]
    public async Task NoArguments_PrintTheSameUsage_AndExitZero()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync([]);

        Assert.Equal(Program.ExitOk, exit);
        foreach (var verb in ExpectedVerbs)
            Assert.Contains(verb, console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Version_ExitsZero()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["--version"]);

        Assert.Equal(Program.ExitOk, exit);
        var expected = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0];
        Assert.Contains(expected, console.Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// A first token that is neither a verb nor a plausible crew path is a typo, and saying so
    /// beats the <c>run</c> parser's answer to the same typo ("script not found: doctr").
    /// </summary>
    [Fact]
    public async Task UnknownVerb_IsRejected()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["doctr"]);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("unknown command", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("doctr", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("--help", console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("script not found", console.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The implicit <c>run</c> stays: a crew path with no verb in front of it still reaches the
    /// run pipeline, and is answered by the run diagnostic rather than by the verb rejection.
    /// </summary>
    [Theory]
    [InlineData("missing-crew.ork.ts")]
    [InlineData("./missing-crew")]
    public async Task ACrewPath_StillRunsWithoutTheRunVerb(string target)
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync([target]);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.DoesNotContain("unknown command", console.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// The usage listing and the dispatch table are two lists of the same verbs, and a verb
    /// added to one and forgotten in the other is exactly the defect this test class exists for:
    /// either an undiscoverable command, or a documented one the CLI answers "unknown command".
    /// </summary>
    [Fact]
    public void TheUsageListing_NamesExactlyTheVerbsTheDispatchKnows()
    {
        var listed = CliUsage.Verbs.Select(v => v.Name).Order(StringComparer.Ordinal).ToList();
        var dispatched = Program.KnownVerbs.Order(StringComparer.Ordinal).ToList();

        Assert.Equal(dispatched, listed);
        Assert.Equal(ExpectedVerbs.Order(StringComparer.Ordinal), listed);
    }

    /// <summary>An option written before any verb belongs to <c>run</c>, and still does.</summary>
    [Fact]
    public async Task AnOptionFirst_StillReachesTheRunParser()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["--events", "not-a-format", "crew.yaml"]);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("unsupported --events format", console.Stderr, StringComparison.Ordinal);
    }
}
