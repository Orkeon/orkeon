using System.Text.Json;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// #429 (and #433's last criterion): a crew that declares <c>decision.mode: required</c> is refused before its
/// first task when the decision circuit is off, with its own exit code, error code and message, in every process.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RequiredDecisionRunTests
{
    private static string Crew(string process) =>
        $$"""
        name: triage-crew
        goal: Triage the tickets
        process: {{process}}
        decision:
          mode: required
        agents:
          reader:
            role: "Reader"
            goal: "Read"
            backstory: "Reads tickets."
            tools: [system_one_decide]

        tasks:
          triage:
            description: "Triage the ticket."
            expected_output: "A category."
            agent: reader
        """;

    private static async Task<(int Exit, TestConsole Console)> RunAsync(ScriptScratch scratch, string process, bool validate = false)
    {
        var crew = scratch.WriteScript("crew.yaml", Crew(process));
        var settings = scratch.WriteFile("appsettings.json", """{ "RaggableTree": { "Enabled": false } }""");
        var console = new TestConsole(stdin: string.Empty);
        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            AllowExternalMounts = true,
            NoDecision = true,
            Validate = validate,
            Events = validate ? null : "jsonl",
        });
        return (exit, console);
    }

    [Theory]
    [InlineData("sequential")]
    [InlineData("parallel")]
    [InlineData("autonomous")]
    [Trait("BR", "CH-429")]
    [Trait("BR", "CH-433")]
    public async Task ShouldRefuseTheRunWithExit69_WhenARequiredCrewRunsWithTheCircuitOff(string process)
    {
        // Arrange
        using var scratch = new ScriptScratch();

        // Act
        var (exit, console) = await RunAsync(scratch, process);

        // Assert
        using var _ = console;
        Assert.True(exit == 69, $"exit {exit}: {console.Stderr}{console.Stdout}");
        Assert.Contains(
            "crew 'triage-crew' requires System One (decision.mode: required) and the decision circuit is off: --no-decision",
            console.Stderr + console.Stdout,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldEmitTheModeAndTheErrorCode_WhenARequiredCrewIsRefused()
    {
        // Arrange
        using var scratch = new ScriptScratch();

        // Act
        var (_, console) = await RunAsync(scratch, "sequential");

        // Assert
        using var _ = console;
        var events = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonElement.Parse(line)).ToList();
        var decision = Assert.Single(events, e => e.GetProperty("kind").GetString() == "run.decision");
        Assert.Equal(
            ("required", "off", "switch"),
            (decision.GetProperty("mode").GetString(), decision.GetProperty("circuit").GetString(), decision.GetProperty("cause").GetString()));
        Assert.Equal("decision_unavailable", Assert.Single(events, e => e.GetProperty("kind").GetString() == "error").GetProperty("code").GetString());
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldWarnAndReportTheMode_WhenValidateMeetsARequiredCrewWithTheCircuitOff()
    {
        // Arrange
        using var scratch = new ScriptScratch();

        // Act
        var (exit, console) = await RunAsync(scratch, "sequential", validate: true);

        // Assert
        using var _ = console;
        Assert.True(exit == Program.ExitOk, $"exit {exit}: {console.Stderr}");
        Assert.Contains("WARNING: crew requires System One; a run would be refused now: the decision circuit is off: --no-decision", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("decision=required)", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldWarnThatTheFallbackIsNeverUsed_WhenValidateMeetsARequiredCrewWithOne()
    {
        // Arrange
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", """
            name: triage-crew
            goal: Triage the tickets
            process: sequential
            decision:
              mode: required
            agents:
              reader: { role: Reader, goal: Read, backstory: Reads tickets. }
            tasks:
              intake: { description: Read the ticket., expected_output: A summary., agent: reader }
              triage:
                description: Route the ticket.
                expected_output: A route.
                decision:
                  questions:
                    route: { choice: { bug: a defect, other: anything else } }
                  state: [intake]
                  fallback: { route: bug }
            """);
        var settings = scratch.WriteFile("appsettings.json", """{ "RaggableTree": { "Enabled": false } }""");
        using var console = new TestConsole(stdin: string.Empty);

        // Act
        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew, SettingsPath = settings, AllowExternalMounts = true, Validate = true,
        });

        // Assert
        Assert.True(exit == Program.ExitOk, $"exit {exit}: {console.Stderr}");
        Assert.Contains(
            "WARNING: Decision task 'triage' declares a fallback, which a crew with decision.mode: required never uses.",
            console.Stderr,
            StringComparison.Ordinal);
    }
}
