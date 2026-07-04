using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// Coverage for the LLM-exchange-log wiring and its external-mount safety guard.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandLlmLogTests
{
    private const string ResultOne =
        "/// <reference orkeon-script=\"1.0\" />\nvar result = 1;\n";

    [Fact]
    public async Task LlmLogPath_outside_cwd_without_allow_flag_is_rejected()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("log.ork.ts", ResultOne);
        // Path.GetTempPath() lives outside the test process working directory.
        var externalLogDir = Path.Combine(Path.GetTempPath(), "ork-llmlog-" + Guid.NewGuid().ToString("N"));
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
            LlmLogPath = externalLogDir,
            AllowExternalMounts = false,
        });

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("--allow-external-mounts is required", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LlmLogPath_outside_cwd_with_allow_flag_runs_and_logs()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("log2.ork.ts", ResultOne);
        var externalLogDir = Path.Combine(Path.GetTempPath(), "ork-llmlog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(externalLogDir); // mount base path must exist before the run
        using var console = new TestConsole();
        try
        {
            var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
            {
                ScriptPath = script,
                LlmLogPath = externalLogDir,
                AllowExternalMounts = true,
                Verbose = 1, // exercise the "LLM exchange logging enabled" log branch
            });

            Assert.Equal(Program.ExitOk, exit);
        }
        finally
        {
            try { Directory.Delete(externalLogDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task LlmLog_flag_inside_cwd_resolves_under_working_directory()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("log3.ork.ts", ResultOne);
        // A directory under the current working directory does not require the allow flag.
        var localLogDir = Path.Combine(Directory.GetCurrentDirectory(),
            "llm-logs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(localLogDir); // mount base path must exist before the run
        using var console = new TestConsole();
        try
        {
            var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
            {
                ScriptPath = script,
                LlmLogEnabled = true,
                LlmLogPath = localLogDir,
            });

            Assert.Equal(Program.ExitOk, exit);
        }
        finally
        {
            try { Directory.Delete(localLogDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
