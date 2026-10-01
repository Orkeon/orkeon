using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// GAP-13: <c>orkeon typings</c> writes the editor typings the tool carries. Before it, a
/// developer who installed the dotnet tool had none: <c>orkeon.d.ts</c> was built inside the
/// repository only, and <c>orkeon-cli.d.ts</c> sat in a DLL resource nothing ever read.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class TypingsCommandTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "orkeon-typings-" + Guid.NewGuid().ToString("N"));

    public TypingsCommandTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch (IOException) { /* best effort */ }
    }

    [Fact]
    public async Task Writes_both_typings_into_dot_orkeon_by_default()
    {
        using var console = new TestConsole();

        var exit = await TypingsCommand.DispatchAsync([], _workspace);

        Assert.Equal(Program.ExitOk, exit);
        var dsl = await File.ReadAllTextAsync(Path.Combine(_workspace, ".orkeon", "orkeon.d.ts"), TestContext.Current.CancellationToken);
        var cli = await File.ReadAllTextAsync(Path.Combine(_workspace, ".orkeon", "orkeon-cli.d.ts"), TestContext.Current.CancellationToken);

        // The DSL roll-up: banner, errors module first, the builders after it.
        Assert.StartsWith("// Orkeon Scripting DSL v1.0", dsl, StringComparison.Ordinal);
        Assert.Contains("Typings/*.d.ts", dsl, StringComparison.Ordinal);
        Assert.Contains("ScriptVersionMismatchError", dsl, StringComparison.Ordinal);
        Assert.Contains("CrewBuilder", dsl, StringComparison.Ordinal);
        Assert.True(
            dsl.IndexOf("ScriptVersionMismatchError", StringComparison.Ordinal) < dsl.IndexOf("CrewBuilder", StringComparison.Ordinal),
            "errors.d.ts must come first in the roll-up");

        // The scripted-command typings.
        Assert.Contains("function defineCommand", cli, StringComparison.Ordinal);
        Assert.Contains("interface CommandRuntimeContext", cli, StringComparison.Ordinal);

        Assert.Contains("orkeon.d.ts", console.Stdout, StringComparison.Ordinal);
        Assert.Contains("/// <reference path=\"./.orkeon/orkeon-cli.d.ts\" />", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Out_chooses_the_directory_and_overwrites_a_stale_copy()
    {
        var target = Path.Combine(_workspace, "crews", ".orkeon");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "orkeon.d.ts"), "// stale", TestContext.Current.CancellationToken);
        using var console = new TestConsole();

        var exit = await TypingsCommand.DispatchAsync(["--out", "crews/.orkeon"], _workspace);

        Assert.Equal(Program.ExitOk, exit);
        var dsl = await File.ReadAllTextAsync(Path.Combine(target, "orkeon.d.ts"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("// stale", dsl, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(target, "orkeon-cli.d.ts")));
    }

    [Fact]
    public async Task An_unknown_argument_is_refused()
    {
        using var console = new TestConsole();

        var exit = await TypingsCommand.DispatchAsync(["--bogus"], _workspace);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("unknown argument '--bogus'", console.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_workspace, ".orkeon")));
    }

    /// <summary>The verb is reachable from the top-level dispatch.</summary>
    [Fact]
    public async Task The_verb_is_dispatched()
    {
        using var console = new TestConsole();

        var exit = await Program.DispatchAsync(["typings", "--help"]);

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("orkeon typings [--out <dir>]", console.Stdout, StringComparison.Ordinal);
    }
}
