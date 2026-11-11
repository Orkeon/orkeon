using System.Text.Json;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// A scripted spawn through <c>orkeon run --events jsonl</c>: the run's publisher reaches the script host
/// (<c>ScriptingHostPorts.RunEvents</c>), so a child a script starts is one <c>agent.spawned</c> line.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandSpawnEventsTests
{
    private const string SpawnScript = """
        const root = agentBuilder().name("root").role("Lead").goal("G")
            .body(async (input, ctx) => { ctx.spawnBackground("kid", "secret-bg-input"); return "root-done"; })
            .build();
        const kid = agentBuilder().name("kid").role("Researcher").goal("G")
            .body(async (input, ctx) => "kid-done")
            .build();
        const crew = crewBuilder().name("c").withAgent(root).withAgent(kid).build();
        await crew.run();
        """;

    [Fact]
    [Trait("BR", "RT-02")]
    public async Task ShouldStreamOneAgentSpawned_WhenAScriptSpawnsABackgroundAgent()
    {
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("spawn.ork.ts", SpawnScript);
        using var console = new TestConsole();

        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions { ScriptPath = script, Events = "jsonl" });

        Assert.True(exit == Program.ExitOk, console.Stderr);
        var spawned = Assert.Single(
            console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonElement.Parse(line)),
            e => e.GetProperty("kind").GetString() == "agent.spawned");
        Assert.Equal("Researcher", spawned.GetProperty("role").GetString());
        Assert.Equal("kid", spawned.GetProperty("name").GetString());
        Assert.DoesNotContain("secret-bg-input", console.Stdout, StringComparison.Ordinal);
    }
}
