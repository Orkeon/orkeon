using System.Text.Json;
using Orkeon.Hosting;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// Coverage for the YAML dispatch added to <see cref="RunCommand"/>: <c>orkeon run crew.yaml</c>
/// must run a crew end-to-end through the shared one-shot runner, while <c>.ork.ts</c> keeps
/// going through the Jint/esbuild script host. Both are driven in-process and offline:
/// no <c>Llm</c> section is configured, so the runner host falls back to the echo provider
/// it announces on stderr (WIN-01) and the crew runs to the end on replayed prompts — the
/// crew runner is reached (its banner is printed) and the command exits
/// <see cref="Program.ExitOk"/>, the contract the routing tests pin alongside the banner.
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
    public async Task Yaml_crew_runs_end_to_end_on_the_echo_provider(string fileName)
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

        // No Llm section: the echo provider answers, announced once on stderr, and the crew
        // completes — exit 0, no "ERROR: " line.
        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains(RunnerHost.LlmNotConfiguredMessage, console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR: ", console.Stderr, StringComparison.Ordinal);
        // The one-shot YAML runner prints a "=== Crew Output ===" banner; the script path
        // never does. Asserting on it pins the routing via observable behavior.
        Assert.Contains("=== Crew Output ===", console.Stdout, StringComparison.Ordinal);
    }

    /// <summary>
    /// GAP-31: the echo provider replays its prompt, so the "plan" it returned declared every task
    /// missing and a <c>planning: true</c> crew exited 2 where the same crew without planning ran
    /// to the end. The plan is skipped instead, with a warning that says so.
    /// </summary>
    [Fact]
    public async Task A_crew_with_planning_runs_to_the_end_on_the_echo_provider_and_says_it_planned_nothing()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript(
            "planned.yaml",
            MinimalCrew.Replace("process: sequential", "process: sequential\nplanning: true", StringComparison.Ordinal));
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = crew,
            AllowExternalMounts = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains("planning skipped", console.Stdout + console.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("ERROR: ", console.Stderr, StringComparison.Ordinal);
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

        // Routed to the crew runner: its banner is printed, the script host's JSON result
        // blob is not.
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

    /// <summary>
    /// GAP-32 decision 4.3: a crew that fails before its first task — here a memory with no
    /// embedder (GAP-30) — reaches the execution hook like any failed run, so the event stream says
    /// <c>error</c> before <c>run.finished</c>, as the protocol promises. No strategy ran, and none
    /// told the hook: the stream used to end on <c>run.finished</c> alone.
    /// </summary>
    [Fact]
    public async Task A_crew_that_fails_before_its_first_task_says_so_on_the_event_stream_before_it_finishes()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript(
            "remembering.yaml",
            MinimalCrew.Replace("process: sequential", "process: sequential\nmemory: true", StringComparison.Ordinal));
        // No embedder at all: the runner's local one comes with RaggableTree, which this run turns off.
        var settings = scratch.WriteFile("appsettings.json", """{ "RaggableTree": { "Enabled": false } }""");
        using var console = new TestConsole(stdin: string.Empty);

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            AllowExternalMounts = true,
            Events = "jsonl",
        });

        Assert.Equal(Program.ExitRuntimeError, exit);
        var events = Events(console.Stdout);
        var kinds = events.Select(e => e.GetProperty("kind").GetString()).ToList();
        var error = Assert.Single(events, e => e.GetProperty("kind").GetString() == "error");
        Assert.Equal("crew_failed", error.GetProperty("code").GetString());
        Assert.Contains("no semantic embedding provider is configured", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.True(kinds.IndexOf("error") < kinds.IndexOf("run.finished"), string.Join(", ", kinds));
        Assert.Equal("run.finished", kinds[^1]);
    }

    /// <summary>
    /// GAP-32 decision 5: <c>--stream</c> puts a crew's agent turns on the stream as they come —
    /// <c>llm.delta</c> used to come from <c>ctx.llm.*</c> only, never from a YAML crew. The echo
    /// provider replays the task's prompt, so the deltas carry it, between the task's start and end.
    /// </summary>
    [Fact]
    public async Task Stream_puts_the_agents_turns_of_a_yaml_crew_on_the_event_stream()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", MinimalCrew);
        using var console = new TestConsole(stdin: string.Empty);

        var exit = await RunCommand.ExecuteAsync(new RunCommandOptions
        {
            ScriptPath = crew,
            AllowExternalMounts = true,
            Events = "jsonl",
            Stream = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        var events = Events(console.Stdout);
        var kinds = events.Select(e => e.GetProperty("kind").GetString()).ToList();
        var deltas = events.Where(e => e.GetProperty("kind").GetString() == "llm.delta").ToList();
        Assert.NotEmpty(deltas);
        Assert.Contains("Say hello.", string.Concat(deltas.Select(d => d.GetProperty("text").GetString())), StringComparison.Ordinal);
        Assert.True(kinds.IndexOf("task.started") < kinds.IndexOf("llm.delta"), string.Join(", ", kinds));
        Assert.True(kinds.LastIndexOf("llm.delta") < kinds.IndexOf("task.completed"), string.Join(", ", kinds));
    }

    /// <summary>The JSON documents a run wrote on stdout, one per line.</summary>
    private static List<JsonElement> Events(string stdout) =>
    [
        .. stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith('{'))
            .Select(line => JsonElement.Parse(line)),
    ];

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
