using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// #433: a crew run by <c>orkeon run --no-decision</c> makes no <see cref="IDecisionModel"/> call - the circuit
/// comes from the switch itself - while the same run without the switch asks the model (here through the LLM
/// judge, fail-open).
/// <para>In the CLI collection: the host it builds logs through the console logger provider, which writes to the process streams.</para>
/// </summary>
[Collection(CliCollection.Name)]
public sealed class RunNoDecisionTests
{
    private const string Crew =
        """
        name: judged-crew
        goal: Draft the notes
        process: sequential

        agents:
          writer:
            role: "Writer"
            goal: "Write"
            backstory: "An agent."

        tasks:
          draft:
            description: "Draft it."
            expected_output: "A draft."
            agent: writer
        """;

    private const string JudgedSettings =
        """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Guardian": { "Llm": { "Enabled": true, "FailMode": "Open" } } } }""";

    private static async Task<(int Exit, string Stderr)> RunAsync(StubDecisionModel model, bool noDecision)
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", Crew);
        var settings = scratch.WriteScript("settings.json", JudgedSettings);
        var answers = scratch.WriteFile(Path.Combine("out", "answers.jsonl"), "{\"match\":\"draft\",\"content\":\"A draft.\"}\n");
        using var console = new TestConsole(stdin: string.Empty);
        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            LlmMock = answers,
            AllowExternalMounts = true,
            NoDecision = noDecision,
            ConfigureTestServices = (_, services) => services.AddSingleton<IDecisionModel>(model),
        });
        return (exit, console.Stderr);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldMakeNoDecisionModelCall_WhenTheCrewRunsUnderNoDecision()
    {
        // Arrange
        var model = new StubDecisionModel { Noul = new() { Noul = 0.1 } };

        // Act
        var (exit, stderr) = await RunAsync(model, noDecision: true);

        // Assert
        Assert.True(exit == Program.ExitOk, $"exit {exit}: {stderr}");
        Assert.Empty(model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldAskTheDecisionModel_WhenTheSameCrewRunsWithoutTheSwitch()
    {
        // Arrange
        var model = new StubDecisionModel { Noul = new() { Noul = 0.1 } };

        // Act
        var (exit, stderr) = await RunAsync(model, noDecision: false);

        // Assert
        Assert.True(exit == Program.ExitOk, $"exit {exit}: {stderr}");
        Assert.NotEmpty(model.Requests);
    }
}
