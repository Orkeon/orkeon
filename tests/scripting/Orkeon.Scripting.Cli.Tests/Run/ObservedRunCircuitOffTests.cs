using System.Text.Json;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// #448: with a decision provider configured and the decision circuit off, <c>system_one_decide</c> resolves to null in
/// the tool collection. A watched run (<c>--events jsonl</c>) wrapped that null in its observing decorator and died
/// before its first event; the run must behave as an unwatched one - the tool withheld, <c>run.decision</c> saying
/// <c>circuit: off</c> with its cause, a <c>required</c> crew refused with exit 69 - and a task that holds a
/// registered tool (<c>human_input</c>) must not meet the null either, watched or not.
/// <para>In the CLI collection: the host it builds logs through the console logger provider, which writes to the process streams.</para>
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ObservedRunCircuitOffTests
{
    // A decision provider is configured (the TypeSafe key): the circuit off comes from the switch or the settings, never
    // from a missing provider. The circuit off, no call ever reaches the provider.
    private const string ProviderSettings =
        """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "TypeSafe": { "ApiKey": "xxxxxxxx" } } }""";

    private const string ProviderSettingsCircuitOff =
        """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "TypeSafe": { "ApiKey": "xxxxxxxx" }, "Decision": { "Enabled": false } } }""";

    // The circuit on, the provider pointed at a closed loopback port with no retry: a call that gets past the
    // tool's own checks fails at once, never reaching a real service.
    private const string ProviderSettingsCircuitOn =
        """{ "RaggableTree": { "Enabled": false }, "Orkeon": { "TypeSafe": { "ApiKey": "xxxxxxxx", "BaseUrl": "http://127.0.0.1:9/", "MaxRetries": 0 } } }""";

    private static string Crew(string decisionMode = "none", bool humanInput = false, string tools = "[]") =>
        $$"""
        name: notes-crew
        goal: Draft the notes
        process: sequential
        decision:
          mode: {{decisionMode}}

        agents:
          writer:
            role: "Writer"
            goal: "Write"
            backstory: "An agent."
            tools: {{tools}}

        tasks:
          draft:
            description: "Draft it."
            expected_output: "A draft."
            agent: writer
            humanInput: {{(humanInput ? "true" : "false")}}
        """;

    private static async Task<(int Exit, TestConsole Console)> RunAsync(
        string crewYaml, string settingsJson, bool noDecision, bool watched = true, string? answersJsonl = null)
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", crewYaml);
        var settings = scratch.WriteFile("appsettings.json", settingsJson);
        var answers = scratch.WriteFile(Path.Combine("out", "answers.jsonl"), answersJsonl ?? "{\"match\":\"Draft it\",\"content\":\"A draft.\"}\n");
        var console = new TestConsole(stdin: string.Empty);
        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            LlmMock = answers,
            AllowExternalMounts = true,
            NoDecision = noDecision,
            Events = watched ? "jsonl" : null,
        });
        return (exit, console);
    }

    private static List<JsonElement> Events(TestConsole console) =>
        console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith('{'))
            .Select(line => JsonElement.Parse(line))
            .ToList();

    [Theory]
    [InlineData(true, "switch")]
    [InlineData(false, "settings")]
    [Trait("BR", "CH-448")]
    public async Task ShouldEmitRunDecisionWithTheCircuitOff_WhenAWatchedRunHasAProviderAndTheCircuitIsOff(bool noDecision, string cause)
    {
        // Arrange
        var settings = noDecision ? ProviderSettings : ProviderSettingsCircuitOff;

        // Act
        var (exit, console) = await RunAsync(Crew(), settings, noDecision);

        // Assert
        using var _ = console;
        Assert.True(exit == Program.ExitOk, $"exit {exit}: {console.Stderr}{console.Stdout}");
        var decision = Assert.Single(Events(console), e => e.GetProperty("kind").GetString() == "run.decision");
        Assert.Equal(
            ("off", cause),
            (decision.GetProperty("circuit").GetString(), decision.GetProperty("cause").GetString()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-448")]
    public async Task ShouldRefuseARequiredCrewWithExit69_WhenAWatchedRunHasAProviderAndTheCircuitIsOff(bool noDecision)
    {
        // Arrange
        var settings = noDecision ? ProviderSettings : ProviderSettingsCircuitOff;

        // Act
        var (exit, console) = await RunAsync(Crew("required"), settings, noDecision);

        // Assert
        using var _ = console;
        Assert.True(exit == 69, $"exit {exit}: {console.Stderr}{console.Stdout}");
        Assert.Equal(
            "decision_unavailable",
            Assert.Single(Events(console), e => e.GetProperty("kind").GetString() == "error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-448")]
    public async Task ShouldRunAHumanInputTask_WhenAProviderIsConfiguredAndTheCircuitIsOff(bool watched)
    {
        // Act
        var (exit, console) = await RunAsync(Crew(humanInput: true), ProviderSettings, noDecision: true, watched);

        // Assert
        using var _ = console;
        Assert.True(exit == Program.ExitOk, $"watched={watched}: exit {exit}: {console.Stderr}{console.Stdout}");
    }

    [Fact]
    [Trait("BR", "CH-448")]
    public async Task ShouldOfferAndObserveSystemOneDecide_WhenAWatchedRunHasTheCircuitOn()
    {
        // Arrange - the agent calls the tool once, then answers
        const string answers =
            "{\"match\":\"Draft it\",\"toolCalls\":[{\"name\":\"system_one_decide\",\"arguments\":{}}]}\n"
            + "{\"match\":\"Draft it\",\"content\":\"A draft.\"}\n";

        // Act
        var (exit, console) = await RunAsync(
            Crew("required", tools: "[system_one_decide]"), ProviderSettingsCircuitOn, noDecision: false, answersJsonl: answers);

        // Assert
        using var _ = console;
        Assert.True(exit == Program.ExitOk, $"exit {exit}: {console.Stderr}{console.Stdout}");
        var events = Events(console);
        Assert.Equal(
            "on",
            Assert.Single(events, e => e.GetProperty("kind").GetString() == "run.decision").GetProperty("circuit").GetString());
        Assert.Contains(events, e => e.GetProperty("kind").GetString() == "tool.called"
            && e.GetProperty("toolName").GetString() == "system_one_decide");
        Assert.Contains(events, e => e.GetProperty("kind").GetString() == "tool.returned");
    }
}
