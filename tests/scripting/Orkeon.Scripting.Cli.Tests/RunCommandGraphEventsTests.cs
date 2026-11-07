using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// FWK-27, through <c>orkeon run --events jsonl</c>: both graph engines say each node they enter as a
/// <c>graph.node.entered</c> line, a Graph-process crew naming its tasks and a script's
/// <c>stateGraph</c> naming its graph.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandGraphEventsTests
{
    private const string GraphCrew = """
        name: graph-events
        goal: Three steps
        process: graph

        agents:
          worker:
            role: "Worker"
            goal: "Work"
            backstory: "A minimal agent."

        tasks:
          first:
            description: "First."
            expected_output: "One."
            agent: worker
          second:
            description: "Second."
            expected_output: "Two."
            agent: worker
          third:
            description: "Third."
            expected_output: "Three."
            agent: worker
        """;

    private const string GraphScript = """
        /// <reference orkeon-script="1.0" />
        const graph = stateGraph({
            name: "pipeline",
            nodes: { draft: (s) => ({ n: s.n + 1 }), review: (s) => s },
            edges: { [START]: "draft", draft: (s) => s.n < 2 ? "draft" : "review", review: END },
        });
        result = await graph.run({ n: 0 });
        """;

    private static List<JsonElement> EnteredLines(TestConsole console) =>
        [.. console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonElement.Parse(line))
            .Where(e => e.GetProperty("kind").GetString() == "graph.node.entered")];

    [Fact]
    [Trait("BR", "FWK-27")]
    public async Task ShouldStreamOneNodeEnteredPerTask_WhenAGraphCrewRuns()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", GraphCrew);
        var settings = scratch.WriteFile("appsettings.json", """{ "RaggableTree": { "Enabled": false } }""");
        var service = new FakeAgentExecutionService();
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            AllowExternalMounts = true,
            Events = "jsonl",
            ConfigureTestServices = (_, services) => services.AddScoped<IAgentExecutionService>(_ => service),
        });

        Assert.True(exit == Program.ExitOk, console.Stderr);
        Assert.Equal(["first", "second", "third"], service.Calls.ToArray());
        var entered = EnteredLines(console);
        Assert.Equal(["first", "second", "third"], entered.Select(e => e.GetProperty("node").GetString()).ToArray());
        Assert.All(entered, e => Assert.Equal(1, e.GetProperty("visit").GetInt32()));
        Assert.All(entered, e => Assert.False(string.IsNullOrEmpty(e.GetProperty("task").GetString())));
    }

    [Fact]
    [Trait("BR", "FWK-27")]
    public async Task ShouldStreamNodeEntered_WhenAScriptRunsAStateGraph()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("graph.ork.ts", GraphScript);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions { ScriptPath = script, Events = "jsonl" });

        Assert.True(exit == Program.ExitOk, console.Stderr);
        var entered = EnteredLines(console);
        Assert.Equal(
            [("draft", 1), ("draft", 2), ("review", 1)],
            entered.Select(e => (e.GetProperty("node").GetString(), e.GetProperty("visit").GetInt32())).ToArray());
        Assert.All(entered, e => Assert.Equal("pipeline", e.GetProperty("graph").GetString()));
    }
}
