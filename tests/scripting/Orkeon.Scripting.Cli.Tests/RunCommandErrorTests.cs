using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// Coverage for the failure-reporting branches of <see cref="RunCommand"/>:
/// esbuild transpile errors (exit 1) and runtime script errors (exit 2).
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandErrorTests
{
    [Fact]
    public async Task Invalid_typescript_is_reported_as_esbuild_rejection_with_ExitScriptError()
    {
        using var scratch = new ScriptScratch();
        // Syntactically broken source — esbuild fails the bundle step and the CLI maps
        // the EsbuildTranspileException onto ExitScriptError.
        var script = scratch.WriteScript("broken.ork.ts", """
            /// <reference orkeon-script="1.0" />
            const = ;;; this is not valid typescript @@@
            """);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("esbuild rejected the script", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_that_throws_at_runtime_is_reported_with_ExitRuntimeError()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("throws.ork.ts", """
            /// <reference orkeon-script="1.0" />
            throw new Error("boom from script");
            """);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
        });

        Assert.Equal(Program.ExitRuntimeError, exit);
        Assert.Contains("unexpected error", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("boom from script", console.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR-008, decision 5, on the scripting path: a user mount claiming a root the runner
    /// needs (/script here, /llm-logs likewise) is a configuration mistake and must read as
    /// one — not as a duplicate-virtual-path exception thrown out of a DI factory.
    /// </summary>
    [Fact]
    public async Task A_user_mount_claiming_the_script_root_is_refused_with_an_actionable_line()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("ok.ork.ts", """
            /// <reference orkeon-script="1.0" />
            """);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
            Mounts = [$"{scratch.OutDir}:/script:rw"],
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("reserved by the runner", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("/script", console.Stderr, StringComparison.Ordinal);
    }
}
