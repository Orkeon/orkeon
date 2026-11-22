using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// A decision task runs no agent and writes nothing: the crew validator asks it for no expected output, and
/// the crew it belongs to loads (#476).
/// <para>In the CLI collection: the host it builds logs through the console logger provider, which writes to the process streams.</para>
/// </summary>
[Collection(CliCollection.Name)]
public sealed class DecisionTaskWithoutExpectedOutputTests
{
    private const string Crew = """
        name: triage-crew
        goal: Triage the tickets
        agents:
          reader: { role: Reader, goal: Read, backstory: Reads tickets. }
        tasks:
          intake: { description: Read the ticket., expected_output: A summary., agent: reader }
          triage:
            description: Route the ticket.
            decision:
              questions:
                route: { choice: { bug: a defect, other: anything else } }
              state: [intake]
              fallback:
                route: other
        """;

    [Fact]
    [Trait("BR", "CH-476")]
    public async Task ShouldLoadTheCrew_WhenItsDecisionTaskDeclaresNoExpectedOutput()
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        using var console = new TestConsole(stdin: string.Empty);

        var exit = await RunCommand.ExecuteAsync(
            console, new RunCommandOptions { ScriptPath = crew, AllowExternalMounts = true, Validate = true });

        Assert.True(exit == Program.ExitOk, $"exit {exit}: {console.Stderr}");
        Assert.DoesNotContain("cannot be null or whitespace", console.Stderr + console.Stdout, StringComparison.Ordinal);
    }
}
