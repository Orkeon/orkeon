using System.Text.Json;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// <c>ctx.llm.pick</c> under <c>orkeon run x.ork.ts --events jsonl</c> (#460): the host wires System One's pick
/// (<c>ScriptingHostPorts.SystemOnePicker</c>) from the run's scope, and its answers are <c>agent.route</c> and
/// <c>agent.effort</c> lines carrying the script agent's role and no task. With no decision model the circuit is off.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunCommandPickEventsTests
{
    private const string PickScript = """
        const planner = agentBuilder().name("planner").role("Planner").goal("G")
            .body(async (input, ctx) => {
                const answer = await ctx.llm.pick("Rename x to count.", { profile: {}, effort: { fallback: "low" } });
                return `${answer.profile.reason}/${answer.effort.value}/${answer.effort.reason}`;
            })
            .build();
        const crew = crewBuilder().name("c").withAgent(planner).build();
        await crew.run();
        """;

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldStreamTheRouteAndEffortOfAPick_WhenAScriptPicksUnderOrkeonRun()
    {
        // Arrange
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("pick.ork.ts", PickScript);
        using var console = new TestConsole();

        // Act
        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions { ScriptPath = script, Events = "jsonl" });

        // Assert
        Assert.True(exit == Program.ExitOk, console.Stderr);
        var events = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith('{'))
            .Select(line => JsonElement.Parse(line))
            .ToList();
        var effort = Assert.Single(events, e => e.GetProperty("kind").GetString() == "agent.effort");
        // A pick belongs to no task: the wire omits a null property, so the line carries no taskId at all.
        Assert.False(effort.TryGetProperty("taskId", out _));
        Assert.Equal("Planner", effort.GetProperty("agentRole").GetString());
        Assert.Equal("circuit-off", effort.GetProperty("reason").GetString());
        var route = Assert.Single(events, e => e.GetProperty("kind").GetString() == "agent.route");
        Assert.False(route.TryGetProperty("taskId", out _));
        Assert.Equal("Planner", route.GetProperty("agentRole").GetString());
    }
}
