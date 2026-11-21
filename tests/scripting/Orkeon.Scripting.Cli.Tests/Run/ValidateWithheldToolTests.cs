using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// #433: <c>orkeon run --validate --no-decision</c> on a crew that lists <c>system_one_decide</c> says,
/// in a <c>WARNING:</c> line, that the tool is withheld and why, leaves it out of the tools resolved
/// and exits 0.
/// <para>In the CLI collection: the host it builds logs through the console logger provider, which writes to the process streams.</para>
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ValidateWithheldToolTests
{
    private const string Crew =
        """
        name: triage-crew
        goal: Triage the tickets
        process: sequential

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

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldWarnAndExitWithZero_WhenValidateMeetsAWithheldTool()
    {
        // Arrange
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        var settings = scratch.WriteFile("appsettings.json", """{ "RaggableTree": { "Enabled": false } }""");
        using var console = new TestConsole(stdin: string.Empty);

        // Act
        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            AllowExternalMounts = true,
            Validate = true,
            NoDecision = true,
        });

        // Assert
        Assert.True(exit == Program.ExitOk, $"exit {exit}: {console.Stderr}");
        Assert.Contains("WARNING: tool 'system_one_decide' withheld: the decision circuit is off: --no-decision", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("tools resolved=0", console.Stdout, StringComparison.Ordinal);
    }
}
