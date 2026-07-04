using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// End-to-end coverage for <see cref="RunCommand"/> driven in-process. Scripts are pure
/// procedural .ork.ts (no LLM call), so the full host bootstrap, esbuild bundling and Jint
/// execution run offline and deterministically.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandTests
{
    private const string ResultOne =
        "/// <reference orkeon-script=\"1.0\" />\nvar result = 42;\n";

    [Fact]
    public void ExecuteAsync_with_null_options_throws_ArgumentNullException()
    {
        // Validation is eager (synchronous) so a null argument surfaces at the call site
        // rather than being captured inside the returned Task. Wrap in an Action so the
        // synchronous throw is observed (not the async overload of Assert.Throws).
        Action act = () => _ = RunCommand.ExecuteAsync(null!);
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public async Task Missing_script_returns_ExitScriptError_and_reports_on_stderr()
    {
        using var scratch = new ScriptScratch();
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = Path.Combine(scratch.ScriptDir, "nope.ork.ts"),
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("script not found", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Simple_script_returns_ExitOk_and_emits_result_JSON()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("hello.ork.ts", ResultOne);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
        });

        Assert.Equal(Program.ExitOk, exit);
        // The result payload is always serialized under a "result" key (ResultJsonOptions).
        Assert.Contains("\"result\"", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_writes_output_through_mounted_directory()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("write.ork.ts", """
            /// <reference orkeon-script="1.0" />
            await tools.fileWrite({ path: "/output/out.txt", content: "ok" });
            """);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
            Mounts = new[] { $"{scratch.OutDir}:/output:rw" },
        });

        Assert.Equal(Program.ExitOk, exit);
        var outPath = Path.Combine(scratch.OutDir, "out.txt");
        Assert.True(File.Exists(outPath));
        Assert.Equal("ok", await File.ReadAllTextAsync(outPath, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Verbose_levels_enable_logging_without_changing_exit(int verbose)
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("v.ork.ts", ResultOne);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
            Verbose = verbose,
            Mounts = new[] { $"{scratch.OutDir}:/output:rw" },
        });

        Assert.Equal(Program.ExitOk, exit);
    }

    [Fact]
    public async Task Verbose_out_of_range_is_clamped_and_still_succeeds()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("clamp.ork.ts", ResultOne);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
            Verbose = 99, // Math.Clamp(_, 0, 2) keeps it at 2.
        });

        Assert.Equal(Program.ExitOk, exit);
    }

    [Fact]
    public async Task Explicit_missing_settings_emits_warning_and_still_runs()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("s.ork.ts", ResultOne);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
            SettingsPath = Path.Combine(scratch.Root, "does-not-exist.json"),
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("settings not found", console.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Explicit_existing_settings_is_used_and_reported()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("s2.ork.ts", ResultOne);
        var settings = scratch.WriteFile("appsettings.json", "{}");
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
            SettingsPath = settings,
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("Using settings", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inputs_options_are_accepted_and_discarded()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("in.ork.ts", ResultOne);
        var inputsFile = scratch.WriteFile("inputs.json", "{\"x\":1}");
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
            InputsJson = "{\"y\":2}",
            InputsFilePath = inputsFile,
        });

        Assert.Equal(Program.ExitOk, exit);
    }

    [Theory]
    [InlineData(64)]  // explicit positive cap
    [InlineData(0)]   // disables the cap (long.MaxValue)
    public async Task Memory_limit_override_is_applied(long limitMb)
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("mem.ork.ts", ResultOne);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
            MemoryLimitMb = limitMb,
            Verbose = 1, // exercise the "memory limit overridden" log branch
        });

        Assert.Equal(Program.ExitOk, exit);
    }
}
