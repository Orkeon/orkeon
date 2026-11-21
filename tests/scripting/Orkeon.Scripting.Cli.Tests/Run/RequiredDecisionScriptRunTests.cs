using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// #429: a declarative <c>.ork.ts</c> crew on <c>.decision({ mode: "required" })</c> is refused before its first task when
/// the decision circuit is off, with the exit code and the message of a YAML crew (<see cref="RequiredDecisionRunTests"/>).
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RequiredDecisionScriptRunTests
{
    private const string Script = """
        // @orkeon-script 1.0
        const reader = agentBuilder().name("reader").role("Reader").goal("Read").backstory("Reads tickets.").build();
        const triage = taskBuilder().name("triage").agent(reader).description("Triage the ticket.").expectedOutput("A category.").build();
        (globalThis as any).crew = crewBuilder().name("triage-crew").goal("Triage the tickets").process("sequential")
            .decision({ mode: "required" })
            .withAgent(reader).withTask(triage)
            .build();
        """;

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldRefuseTheRunWithExit69_WhenARequiredScriptedCrewRunsWithTheCircuitOff()
    {
        // Arrange
        using var scratch = new ScriptScratch();
        var script = scratch.WriteScript("crew.ork.ts", Script);
        var settings = scratch.WriteFile("appsettings.json", """{ "RaggableTree": { "Enabled": false } }""");
        using var console = new TestConsole(stdin: string.Empty);

        // Act
        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = script,
            SettingsPath = settings,
            AllowExternalMounts = true,
            NoDecision = true,
        });

        // Assert
        Assert.True(exit == 69, $"exit {exit}: {console.Stderr}{console.Stdout}");
        Assert.Contains(
            "crew 'triage-crew' requires System One (decision.mode: required) and the decision circuit is off: --no-decision",
            console.Stderr + console.Stdout,
            StringComparison.Ordinal);
    }
}
