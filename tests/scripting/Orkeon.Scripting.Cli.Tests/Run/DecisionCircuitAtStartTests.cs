using System.Text.Json;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>
/// What the decision circuit makes certain before the first task: a hosted decision provider without its key turns
/// the circuit off by <c>no-key</c> (#451), and a fail-closed decision profile with a guard blocks every call with
/// the circuit off, which <c>--validate</c> says (#452).
/// </summary>
[Collection(CliCollection.Name)]
public sealed class DecisionCircuitAtStartTests
{
    // A variable no machine sets, so the key is absent whatever the environment of the test pass.
    private const string NoKeyVariable = "ORKEON_TEST_451_NO_SUCH_KEY";

    private const string NoKeySettings =
        $$"""{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Liquid": { "Model": "lfm", "ApiKeyEnvVar": "{{NoKeyVariable}}" } } }""";

    // The variable is not the provider's default: the sentence names the setting paths, never the value (#467).
    private const string NoKeySentence =
        "no Liquid API key: set the variable named by Orkeon:Liquid:ApiKeyEnvVar, or Orkeon:Liquid:ApiKey";

    private const string NoKeyReason = "the decision circuit is off: " + NoKeySentence;

    private const string RequiredCrew = """
        name: triage-crew
        goal: Triage the tickets
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
        """;

    private static string GuardedSettings(bool failClosed, bool enabled = true) => $$"""
        { "RaggableTree": { "Enabled": false },
          "Orkeon": { "Decision": { "Enabled": {{(enabled ? "true" : "false")}}, "Profiles": { "guarded": {
            "Target": "default", "FailClosed": {{(failClosed ? "true" : "false")}},
            "Stages": [ { "Kind": "guard", "Risks": { "x": "y" } } ] } } } } }
        """;

    private const string GuardedCrew = """
        name: guarded-crew
        goal: Answer
        agents:
          a: { role: A, goal: g, backstory: b, llm: { profile: guarded } }
        tasks:
          answer: { description: Answer., expected_output: An answer., agent: a }
        """;

    private static async Task<(int Exit, TestConsole Console)> RunAsync(
        string crewYaml, string settingsJson, bool validate, bool noDecision = false, bool events = true)
    {
        using var scratch = new ScriptScratch();
        var crew = scratch.WriteScript("crew.yaml", crewYaml);
        var settings = scratch.WriteFile("appsettings.json", settingsJson);
        var console = new TestConsole(stdin: string.Empty);
        var exit = await RunCommand.ExecuteAsync(console, new RunCommandOptions
        {
            ScriptPath = crew,
            SettingsPath = settings,
            AllowExternalMounts = true,
            NoDecision = noDecision,
            Validate = validate,
            Events = validate || !events ? null : "jsonl",
        });
        return (exit, console);
    }

    [Fact]
    [Trait("BR", "CH-451")]
    public async Task ShouldExit69BeforeTheFirstTaskNamingTheMissingKey_WhenARequiredCrewRunsOnAHostedProviderWithoutItsKey()
    {
        // Act: no event stream - an observed run with the circuit off is #448's, fixed apart; the cause on
        // run.decision is RunDecisionEventTests'.
        var (exit, console) = await RunAsync(RequiredCrew, NoKeySettings, validate: false, events: false);

        // Assert
        using var _ = console;
        Assert.True(exit == 69, $"exit {exit}: {console.Stderr}{console.Stdout}");
        Assert.Contains(
            "crew 'triage-crew' requires System One (decision.mode: required) and " + NoKeyReason,
            console.Stderr + console.Stdout,
            StringComparison.Ordinal);
    }

    private static JsonElement RunDecision(TestConsole console) =>
        Assert.Single(
            console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith('{'))
                .Select(line => JsonElement.Parse(line)),
            e => e.GetProperty("kind").GetString() == "run.decision");

    [Fact]
    [Trait("BR", "CH-467")]
    public async Task ShouldCarryTheCircuitsSentenceOnRunDecision_WhenAWatchedRunHasNoKey()
    {
        // Act
        var (exit, console) = await RunAsync(RequiredCrew, NoKeySettings, validate: false);

        // Assert
        using var _ = console;
        Assert.True(exit == 69, $"exit {exit}: {console.Stderr}{console.Stdout}");
        var decision = RunDecision(console);
        Assert.Equal(
            ("no-key", NoKeySentence),
            (decision.GetProperty("cause").GetString(), decision.GetProperty("detail").GetString()));
        Assert.DoesNotContain(NoKeyVariable, console.Stdout + console.Stderr, StringComparison.Ordinal);
    }

    // Keys pasted into the variable setting by mistake, built at run time: a dashed one, and one made of
    // letters, digits and underscores only, the shape of a variable name.
    public static TheoryData<string> PastedKeys =>
        [string.Concat("sk", "-ant-test-", "0123456789abcdef"), string.Concat("lk", "_live_", "0123456789ABCDEFabcdef")];

    [Theory]
    [MemberData(nameof(PastedKeys))]
    [Trait("BR", "CH-467")]
    public async Task ShouldNeverWriteAKeyPastedIntoTheVariableSetting_WhenAWatchedRunHasNoKey(string pasted)
    {
        // Arrange
        var settings =
            $$"""{ "RaggableTree": { "Enabled": false }, "Orkeon": { "Liquid": { "Model": "lfm", "ApiKeyEnvVar": "{{pasted}}" } } }""";

        // Act
        var (exit, console) = await RunAsync(RequiredCrew, settings, validate: false);

        // Assert
        using var _ = console;
        Assert.True(exit == 69, $"exit {exit}: {console.Stderr}{console.Stdout}");
        Assert.Equal(NoKeySentence, RunDecision(console).GetProperty("detail").GetString());
        Assert.DoesNotContain(pasted, console.Stdout + console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-451")]
    public async Task ShouldWarn_WhenValidateMeetsARequiredCrewOnAHostedProviderWithoutItsKey()
    {
        // Act
        var (exit, console) = await RunAsync(RequiredCrew, NoKeySettings, validate: true);

        // Assert
        using var _ = console;
        Assert.True(exit == Program.ExitOk, $"exit {exit}: {console.Stderr}");
        Assert.Contains("WARNING: crew requires System One; a run would be refused now: " + NoKeyReason, console.Stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true, "--no-decision")]
    [InlineData(false, false, "Orkeon:Decision:Enabled=false")]
    [InlineData(false, true, "no decision provider resolved")]
    [Trait("BR", "CH-452")]
    public async Task ShouldWarnThatEveryCallIsBlocked_WhenValidateMeetsAnAgentOnAFailClosedGuardedProfileWithTheCircuitOff(
        bool noDecision, bool enabled, string reason)
    {
        // Act
        var (exit, console) = await RunAsync(GuardedCrew, GuardedSettings(failClosed: true, enabled), validate: true, noDecision);

        // Assert
        using var _ = console;
        Assert.True(exit == Program.ExitOk, $"exit {exit}: {console.Stderr}");
        Assert.Contains(
            "WARNING: agent 'A' runs on the decision profile 'guarded', fail-closed with a guard stage: a run would block every call " +
            $"through it now (the decision circuit is off: {reason})",
            console.Stderr,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-452")]
    public async Task ShouldNotWarnOfTheProfile_WhenTheGuardedProfileIsFailOpen()
    {
        // Act
        var (exit, console) = await RunAsync(GuardedCrew, GuardedSettings(failClosed: false), validate: true, noDecision: true);

        // Assert
        using var _ = console;
        Assert.True(exit == Program.ExitOk, $"exit {exit}: {console.Stderr}");
        Assert.DoesNotContain("decision profile 'guarded'", console.Stderr, StringComparison.Ordinal);
    }
}
