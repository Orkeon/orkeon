using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// Coverage for the YAML dispatch added to <see cref="RunCommand"/>: <c>orkeon run crew.yaml</c>
/// must run a crew end-to-end through the shared one-shot runner, while <c>.ork.ts</c> keeps
/// going through the Jint/esbuild script host. Both are driven in-process and offline:
/// no <c>Llm</c> section is configured, so the default provider returns an empty completion
/// (0 chars) and the crew finishes cleanly with empty output — same "provider absent" stance
/// as the existing procedural-script smoke tests.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandYamlTests
{
    /// <summary>Minimal deterministic crew: 1 agent, 1 task, 0 tools, sequential, no LLM section.</summary>
    private const string MinimalCrew =
        """
        name: smoke-crew
        goal: Say hello deterministically
        process: sequential

        agents:
          greeter:
            role: "Greeter"
            goal: "Greet"
            backstory: "A minimal agent for smoke testing."

        tasks:
          greet:
            description: "Say hello."
            expected_output: "A greeting."
            agent: greeter
        """;

    [Theory]
    [InlineData("crew.yaml")]
    [InlineData("crew.yml")]
    public async Task Yaml_crew_runs_end_to_end_and_returns_ExitOk(string fileName)
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript(fileName, MinimalCrew);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = crew,
            // The scratch dir lives under the temp path (outside the test cwd), so the
            // runner's external-config guard requires the explicit opt-in.
            AllowExternalMounts = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        // The one-shot YAML runner prints a "=== Crew Output ===" banner; the script path
        // never does. Asserting on it pins the routing via observable behavior.
        Assert.Contains("=== Crew Output ===", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Yaml_extension_routes_to_crew_runner_not_script_host()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("routing.yaml", MinimalCrew);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = crew,
            AllowExternalMounts = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        // Crew-runner marker present, script-host marker absent.
        Assert.Contains("=== Crew Output ===", console.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("\"result\"", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrkTs_extension_still_routes_to_script_host_not_crew_runner()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("routing.ork.ts",
            "/// <reference orkeon-script=\"1.0\" />\nvar result = 42;\n");
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = script,
        });

        Assert.Equal(Program.ExitOk, exit);
        // Script-host marker present, crew-runner marker absent.
        Assert.Contains("\"result\"", console.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("=== Crew Output ===", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_yaml_crew_returns_ExitScriptError()
    {
        using var scratch = new ScriptScratch();
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = Path.Combine(scratch.ScriptDir, "nope.yaml"),
            AllowExternalMounts = true,
        });

        // RunOneShotAsync reports a missing config with exit code 1 (== ExitScriptError).
        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("config file not found", console.Stderr, StringComparison.OrdinalIgnoreCase);
    }
}
