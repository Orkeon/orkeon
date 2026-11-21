using System.Text.Json;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// <c>--no-decision</c> at the command line (#435): accepted by a new session and a resume, never stored,
/// <c>session.started</c> names the forge's circuit and its cause, and the crew's own circuit is untouched.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ForgeSystemOneCommandTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-system-one-command-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    /// <summary>No decision model.</summary>
    private const string NoProvider = """{ "Llm": { "Model": "never-called", "BaseUrl": "http://127.0.0.1:9" } }""";

    /// <summary>A decision model registered and never called: the run stops at the dry pause.</summary>
    private const string WithProvider = """
        { "Llm": { "Model": "never-called", "BaseUrl": "http://127.0.0.1:9" },
          "Orkeon": { "Decision": { "Provider": "kev" }, "Kev": { "Model": "kev-9b", "BaseUrl": "http://127.0.0.1:9/v1" } } }
        """;

    /// <summary>A decision model registered, the circuit turned off by the settings.</summary>
    private const string DisabledBySettings = """
        { "Llm": { "Model": "never-called", "BaseUrl": "http://127.0.0.1:9" },
          "Orkeon": { "Decision": { "Provider": "kev", "Enabled": false }, "Kev": { "Model": "kev-9b", "BaseUrl": "http://127.0.0.1:9/v1" } } }
        """;

    private string WriteSettings(string json)
    {
        Directory.CreateDirectory(_workspace);
        var path = Path.Combine(_workspace, "settings.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>A session parked at the dry pause with the triage plan, in <paramref name="mode"/>.</summary>
    private ForgeSession SessionAtDryPause(string mode)
    {
        var session = ForgeSession.Create(_workspace, "tri", crewDecision: mode);
        Assert.True(ForgeBlueprint.TryParse(ForgeCrewDecisionPlanTests.TriageBlueprint, out var blueprint, out _, [], ForgeCrewDecision.Required));
        session.SaveArtifact(ForgeSession.BlueprintFileName, blueprint!);
        ForgeYamlRenderer.Render(ForgeBlueprintCompiler.Compile(blueprint!, crewDecision: mode), session.Directory);
        session.Document.Budget.RegisterIteration();
        session.Document.Iteration = 1;
        session.SetState(ForgeState.Test);
        session.Save(DateTimeOffset.UtcNow);
        return session;
    }

    private async Task<(int ExitCode, IReadOnlyList<JsonElement> Events)> ResumeDryAsync(string settings, params string[] extra)
    {
        using var console = new TestConsole();
        var exitCode = await ForgeCommand.DispatchAsync(
            console, ["resume", "tri", "--dry", "--events", "jsonl", "--settings", settings, .. extra], _workspace);
        return (exitCode, [.. console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonElement.Parse(line))]);
    }

    private static JsonElement Started(IReadOnlyList<JsonElement> events) =>
        events.Single(e => e.GetProperty("kind").GetString() == "session.started");

    [Fact]
    public void A_new_session_accepts_the_switch()
    {
        var options = ForgeCommandOptions.Parse(["une", "veille", "--no-decision"]);

        Assert.Null(options.Error);
    }

    [Fact]
    public void A_resume_accepts_the_switch()
    {
        var options = ForgeCommandOptions.Parse(["resume", "veille", "--no-decision"]);

        Assert.Null(options.Error);
    }

    [Fact]
    [Trait("BR", "CH-435")]
    public void List_refuses_the_switch_as_an_argument()
    {
        // Arrange
        string[] args = ["list", "--no-decision"];

        // Act
        var options = ForgeCommandOptions.Parse(args);

        // Assert
        Assert.Equal("list takes no argument.", options.Error);
    }

    [Fact]
    public void The_switch_reads_as_no_decision()
    {
        var options = ForgeCommandOptions.Parse(["une", "veille", "--no-decision"]);

        Assert.True(options.NoDecision);
    }

    public static TheoryData<string, string[], string, string?> Circuits => new()
    {
        { WithProvider, ["--no-decision"], "off", "switch" },
        { DisabledBySettings, [], "off", "settings" },
        { NoProvider, [], "off", "no-provider" },
        { WithProvider, [], "on", null },
    };

    [Theory]
    [MemberData(nameof(Circuits))]
    public async Task Session_started_names_the_forge_circuit_of_each_cause(
        string settings, string[] extra, string circuit, string? cause)
    {
        SessionAtDryPause(ForgeCrewDecision.Optional);

        var (_, events) = await ResumeDryAsync(WriteSettings(settings), extra);

        var started = Started(events);
        Assert.Equal(circuit, started.GetProperty("forgeCircuit").GetString());
        Assert.Equal(cause, started.TryGetProperty("forgeCircuitCause", out var c) ? c.GetString() : null);
    }

    [Fact]
    public async Task The_switch_is_not_stored_with_the_session()
    {
        var session = SessionAtDryPause(ForgeCrewDecision.Optional);

        await ResumeDryAsync(WriteSettings(WithProvider), "--no-decision");

        var document = await File.ReadAllTextAsync(Path.Combine(session.Directory, "session.json"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain("noDecision", document, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("forgeCircuit", document, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_required_crew_keeps_its_trial_circuit_under_the_switch()
    {
        SessionAtDryPause(ForgeCrewDecision.Required);

        var (exitCode, events) = await ResumeDryAsync(WriteSettings(WithProvider), "--no-decision");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain(events, e => e.TryGetProperty("code", out var code) && code.GetString() == ForgeCrewDecision.UnavailableCode);
    }

    [Fact]
    public async Task Session_started_keeps_the_crews_mode_beside_the_forge_circuit()
    {
        SessionAtDryPause(ForgeCrewDecision.Required);

        var (_, events) = await ResumeDryAsync(WriteSettings(WithProvider), "--no-decision");

        Assert.Equal("required", Started(events).GetProperty("crewDecision").GetString());
    }
}
