using System.Text.Json;
using Orkeon.Application.Crew.Execution;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// <c>--crew-decision</c> at the command line (#431): the option is recorded by a new session, refused when it would
/// change on resume, derived for a session older than it, and a required crew is refused its trial without System One.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ForgeCrewDecisionCommandTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-crew-decision-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    /// <summary>A settings file whose Llm section satisfies the host gate, with no decision model; nothing is called.</summary>
    private string WriteSettings()
    {
        Directory.CreateDirectory(_workspace);
        var path = Path.Combine(_workspace, "settings.json");
        File.WriteAllText(path, """{ "Llm": { "Model": "never-called", "BaseUrl": "http://127.0.0.1:9" } }""");
        return path;
    }

    /// <summary>A settings file whose Llm section satisfies the host gate, with the decision circuit off by the settings.</summary>
    private string WriteSettingsWithTheCircuitOff()
    {
        Directory.CreateDirectory(_workspace);
        var path = Path.Combine(_workspace, "settings-off.json");
        File.WriteAllText(path, """{ "Llm": { "Model": "never-called", "BaseUrl": "http://127.0.0.1:9" }, "Orkeon": { "Decision": { "Enabled": false } } }""");
        return path;
    }

    /// <summary>A session parked at the dry pause with the triage plan, in <paramref name="mode"/>.</summary>
    private ForgeSession SessionAtDryPause(string? mode)
    {
        var session = ForgeSession.Create(_workspace, "tri", crewDecision: mode ?? ForgeCrewDecision.None);
        Assert.True(ForgeBlueprint.TryParse(ForgeCrewDecisionPlanTests.TriageBlueprint, out var blueprint, out _, [], ForgeCrewDecision.Required));
        session.SaveArtifact(ForgeSession.BlueprintFileName, blueprint!);
        ForgeYamlRenderer.Render(ForgeBlueprintCompiler.Compile(blueprint!, crewDecision: mode ?? ForgeCrewDecision.Required), session.Directory);
        session.Document.Budget.RegisterIteration();
        session.Document.Iteration = 1;
        session.Document.CrewDecision = mode;
        session.SetState(ForgeState.Test);
        session.Save(DateTimeOffset.UtcNow);
        return session;
    }

    [Theory]
    [InlineData("none")]
    [InlineData("optional")]
    [InlineData("Required")]
    public void The_option_reads_each_mode(string mode)
    {
        var options = ForgeCommandOptions.Parse(["une", "veille", "--crew-decision", mode]);

        Assert.Null(options.Error);
    }

    [Fact]
    public async Task An_unknown_mode_is_refused_with_the_three_real_ones()
    {
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console, ["un", "besoin", "--crew-decision", "maybe"], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("unknown crew decision mode 'maybe' — use none, optional or required", console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_session_records_its_mode_in_session_json()
    {
        var session = ForgeSession.Create(_workspace, "tri", crewDecision: ForgeCrewDecision.Optional);

        var document = JsonElement.Parse(File.ReadAllText(Path.Combine(session.Directory, "session.json")));

        Assert.Equal("optional", document.GetProperty("crewDecision").GetString());
    }

    [Fact]
    public async Task A_resume_cannot_change_the_crew_decision_mode()
    {
        var session = SessionAtDryPause(ForgeCrewDecision.Optional);
        var before = SessionFiles(session);
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--crew-decision", "required"], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("was created with --crew-decision optional", console.Stderr, StringComparison.Ordinal);
        // Refused before saving anything: the stored mode, state and history are as they were.
        Assert.Equal(before, SessionFiles(session));
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "tri", out var reloaded, out _));
        Assert.Equal((ForgeCrewDecision.Optional, ForgeState.Test), (reloaded!.Document.CrewDecision, reloaded.State));
    }

    /// <summary>The session's own record files, by name, as they are on disk: what a refusal must leave untouched.</summary>
    private static IReadOnlyList<(string Name, string Text)> SessionFiles(ForgeSession session) =>
    [
        .. new[] { "session.json", ForgeSession.HistoryFileName, ForgeSession.BlueprintFileName }
            .Select(name => Path.Combine(session.Directory, name))
            .Where(File.Exists)
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path))),
    ];

    [Fact]
    public async Task A_legacy_session_derives_its_mode_from_its_plan_and_warns()
    {
        var settings = WriteSettings();
        var session = SessionAtDryPause(mode: null);
        using var console = new TestConsole();

        await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--dry", "--events", "jsonl", "--settings", settings], _workspace);

        Assert.Contains(ForgeCrewDecision.DerivedWarning, console.Stdout, StringComparison.Ordinal);
        var document = JsonElement.Parse(await File.ReadAllTextAsync(Path.Combine(session.Directory, "session.json"), TestContext.Current.CancellationToken));
        Assert.Equal("required", document.GetProperty("crewDecision").GetString());
    }

    [Fact]
    public async Task A_required_crew_is_refused_its_trial_when_no_decision_model_is_configured()
    {
        var settings = WriteSettings();
        SessionAtDryPause(ForgeCrewDecision.Required);
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--settings", settings], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains(ForgeCrewDecision.UnavailableCode, console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_required_crew_refusal_is_on_the_stream_with_its_cause()
    {
        var settings = WriteSettings();
        SessionAtDryPause(ForgeCrewDecision.Required);
        using var console = new TestConsole();

        await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--events", "jsonl", "--settings", settings], _workspace);

        var error = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonElement.Parse(line))
            .Single(e => e.GetProperty("kind").GetString() == "error");
        Assert.Equal(ForgeCrewDecision.UnavailableCode, error.GetProperty("code").GetString());
        Assert.Equal("no-provider", error.GetProperty("cause").GetString());
        Assert.True(error.GetProperty("terminal").GetBoolean());
    }

    [Fact]
    public async Task A_required_crew_refusal_names_the_settings_when_they_turn_the_circuit_off()
    {
        var settings = WriteSettingsWithTheCircuitOff();
        SessionAtDryPause(ForgeCrewDecision.Required);
        using var console = new TestConsole();

        await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--events", "jsonl", "--settings", settings], _workspace);

        var error = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonElement.Parse(line))
            .Single(e => e.GetProperty("kind").GetString() == "error");
        Assert.Equal((ForgeCrewDecision.UnavailableCode, "settings"), (error.GetProperty("code").GetString(), error.GetProperty("cause").GetString()));
        // #467: a cause without a sentence writes no detail.
        Assert.False(error.TryGetProperty("detail", out _));
    }

    /// <summary>#467 — a hosted decision provider without its key: the refusal on the stream carries where the key is read from.</summary>
    [Fact]
    [Trait("BR", "CH-467")]
    public async Task A_required_crew_refusal_for_a_missing_key_carries_the_circuits_detail()
    {
        const string Variable = "ORKEON_TEST_467_NO_SUCH_KEY";
        Directory.CreateDirectory(_workspace);
        var settings = Path.Combine(_workspace, "settings-no-key.json");
        await File.WriteAllTextAsync(settings,
            $$"""{ "Llm": { "Model": "never-called", "BaseUrl": "http://127.0.0.1:9" }, "Orkeon": { "Liquid": { "Model": "lfm", "ApiKeyEnvVar": "{{Variable}}" } } }""",
            TestContext.Current.CancellationToken);
        SessionAtDryPause(ForgeCrewDecision.Required);
        using var console = new TestConsole();

        await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--events", "jsonl", "--settings", settings], _workspace);

        var events = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonElement.Parse(line))
            .ToList();
        var error = events.Single(e => e.GetProperty("kind").GetString() == "error");
        var started = events.Single(e => e.GetProperty("kind").GetString() == "session.started");
        // Not the provider's default variable: the sentence names the setting paths, never the value.
        const string Sentence = "no Liquid API key: set the variable named by Orkeon:Liquid:ApiKeyEnvVar, or Orkeon:Liquid:ApiKey";
        Assert.Equal((ForgeCrewDecision.UnavailableCode, "no-key"), (error.GetProperty("code").GetString(), error.GetProperty("cause").GetString()));
        Assert.Equal(Sentence, error.GetProperty("detail").GetString());
        Assert.Equal(("no-key", Sentence), (started.GetProperty("forgeCircuitCause").GetString(), started.GetProperty("forgeCircuitDetail").GetString()));
        Assert.DoesNotContain(Variable, console.Stdout + console.Stderr, StringComparison.Ordinal);
    }

    /// <summary>Keys pasted into <c>ApiKeyEnvVar</c> by mistake, built at run time: a dashed one, and one shaped like a variable name.</summary>
    public static TheoryData<string> PastedKeys =>
        [string.Concat("sk", "-ant-test-", "0123456789abcdef"), string.Concat("lk", "_live_", "0123456789ABCDEFabcdef")];

    /// <summary>#467 — a key pasted into <c>ApiKeyEnvVar</c> never reaches the forge's events: the sentence names the setting.</summary>
    [Theory]
    [MemberData(nameof(PastedKeys))]
    [Trait("BR", "CH-467")]
    public async Task A_key_pasted_into_the_variable_setting_never_reaches_the_forges_events(string pasted)
    {
        Directory.CreateDirectory(_workspace);
        var settings = Path.Combine(_workspace, "settings-pasted-key.json");
        await File.WriteAllTextAsync(settings,
            $$"""{ "Llm": { "Model": "never-called", "BaseUrl": "http://127.0.0.1:9" }, "Orkeon": { "Liquid": { "Model": "lfm", "ApiKeyEnvVar": "{{pasted}}" } } }""",
            TestContext.Current.CancellationToken);
        SessionAtDryPause(ForgeCrewDecision.Required);
        using var console = new TestConsole();

        await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--events", "jsonl", "--settings", settings], _workspace);

        var events = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonElement.Parse(line))
            .ToList();
        var error = events.Single(e => e.GetProperty("kind").GetString() == "error");
        var started = events.Single(e => e.GetProperty("kind").GetString() == "session.started");
        const string Sentence = "no Liquid API key: set the variable named by Orkeon:Liquid:ApiKeyEnvVar, or Orkeon:Liquid:ApiKey";
        Assert.Equal((Sentence, Sentence), (error.GetProperty("detail").GetString(), started.GetProperty("forgeCircuitDetail").GetString()));
        Assert.DoesNotContain(pasted, console.Stdout + console.Stderr, StringComparison.Ordinal);
    }

    /// <summary>A missing key is not cured by turning the circuit on: the refusal names the key to give, not the switch.</summary>
    [Fact]
    public void A_required_crew_refusal_for_a_missing_key_asks_for_the_key()
    {
        var circuit = new DecisionCircuit(Enabled: false)
        {
            Cause = DecisionCircuitCause.NoKey,
            Detail = "no Liquid API key: set the environment variable LIQUID_API_KEY, or Orkeon:Liquid:ApiKey",
        };

        var message = ForgeCommand.UnavailableDecisionMessage(circuit);

        Assert.Contains("set the environment variable LIQUID_API_KEY", message, StringComparison.Ordinal);
        Assert.EndsWith("give the decision provider its key before its trial.", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Orkeon:Decision:Enabled=true", message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_crew_decision_option_is_refused_by_promote()
    {
        string[] args = ["promote", "tri", "--to", "/teams/tri", "--crew-decision", "optional"];

        var options = ForgeCommandOptions.Parse(args);

        Assert.Equal("--crew-decision only applies to a new session or to `forge resume`.", options.Error);
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public void The_crew_decision_option_is_refused_by_list()
    {
        // Arrange
        string[] args = ["list", "--crew-decision", "optional"];

        // Act
        var options = ForgeCommandOptions.Parse(args);

        // Assert
        Assert.Equal("list takes no argument.", options.Error);
    }

    [Fact]
    public async Task A_new_required_session_is_refused_before_its_interview_without_system_one()
    {
        var settings = WriteSettings();
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console, ["trier", "les", "demandes", "--crew-decision", "required", "--settings", settings], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains(ForgeCrewDecision.UnavailableCode, console.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task A_new_required_session_refused_without_system_one_leaves_no_session()
    {
        // Arrange
        var settings = WriteSettings();
        using var console = new TestConsole();

        // Act
        var exitCode = await ForgeCommand.DispatchAsync(console, ["trier", "les", "demandes", "--crew-decision", "required", "--settings", settings], _workspace);

        // Assert: no folder at all under the sessions root, a half-deleted one included.
        Assert.Equal(1, exitCode);
        Assert.Contains(ForgeCrewDecision.UnavailableCode, console.Stderr, StringComparison.Ordinal);
        var root = ForgeSession.RootFor(_workspace);
        Assert.Empty(Directory.Exists(root) ? Directory.EnumerateFileSystemEntries(root) : []);
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task A_refused_resume_of_a_required_session_keeps_the_session()
    {
        // Arrange
        var settings = WriteSettings();
        var session = SessionAtDryPause(ForgeCrewDecision.Required);
        var before = SessionFiles(session);
        using var console = new TestConsole();

        // Act
        var exitCode = await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--settings", settings], _workspace);

        // Assert: refused, nothing saved, and the session still loads at its pause, resumable.
        Assert.Equal(1, exitCode);
        Assert.Contains(ForgeCrewDecision.UnavailableCode, console.Stderr, StringComparison.Ordinal);
        Assert.Equal(before, SessionFiles(session));
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "tri", out var reloaded, out var error), error);
        Assert.Equal((ForgeState.Test, ForgeSessionStatus.Active), (reloaded!.State, reloaded.Status));
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public void Adopting_with_no_decision_is_refused_as_an_option_it_would_ignore()
    {
        // Arrange
        string[] args = ["resume", "tri", "--adopt", "--no-decision"];

        // Act
        var options = ForgeCommandOptions.Parse(args);

        // Assert
        Assert.Equal("--no-decision does not apply to `--adopt`: the adoption asks System One nothing.", options.Error);
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task Reopening_the_folder_of_a_legacy_session_announces_the_mode_its_resume_will_use()
    {
        // Arrange: a triage session promoted to a team folder, then stripped of its mode as a session from before
        // --crew-decision has none.
        var session = SessionAtDryPause(ForgeCrewDecision.Required);
        session.SetState(ForgeState.Ready);
        session.SetStatus(ForgeSessionStatus.Ready);
        session.Save(DateTimeOffset.UtcNow);
        var team = Path.Combine(_workspace, "teams", "tri");
        using (var promote = new TestConsole())
            Assert.Equal(0, await ForgeCommand.DispatchAsync(promote, ["promote", "tri", "--to", team, "--events", "jsonl"], _workspace));
        var path = Path.Combine(session.Directory, "session.json");
        var document = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!.AsObject();
        Assert.True(document.Remove("crewDecision"));
        await File.WriteAllTextAsync(path, document.ToJsonString(), TestContext.Current.CancellationToken);
        using var console = new TestConsole();

        // Act
        var exitCode = await ForgeCommand.DispatchAsync(console, ["reopen", team, "--events", "jsonl"], _workspace);

        // Assert
        Assert.Equal(0, exitCode);
        var started = console.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonElement.Parse(line))
            .Single(e => e.GetProperty("kind").GetString() == "session.started");
        Assert.Equal(ForgeCrewDecision.Required, started.GetProperty("crewDecision").GetString());
    }

    [Fact]
    public async Task Adopting_a_required_crew_without_system_one_is_not_refused()
    {
        SessionAtDryPause(ForgeCrewDecision.Required);
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--adopt"], _workspace);

        Assert.Equal(0, exitCode);
    }

    private static IReadOnlyList<JsonElement> Events(string stdout) =>
        [.. stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonElement.Parse(line))];

    private static string? Kind(JsonElement e) => e.GetProperty("kind").GetString();

    private static string? Code(JsonElement e) => e.TryGetProperty("code", out var code) ? code.GetString() : null;

    private static string? RecordedMode(ForgeSession session)
    {
        var document = JsonElement.Parse(File.ReadAllText(Path.Combine(session.Directory, "session.json")));
        return document.TryGetProperty("crewDecision", out var mode) && mode.ValueKind == JsonValueKind.String ? mode.GetString() : null;
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task Adopting_a_legacy_session_derives_its_mode_and_warns_after_session_started()
    {
        // Arrange
        var session = SessionAtDryPause(mode: null);
        using var console = new TestConsole();

        // Act
        var exitCode = await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--adopt", "--events", "jsonl"], _workspace);

        // Assert
        Assert.Equal(0, exitCode);
        var events = Events(console.Stdout);
        Assert.Equal(["session.started", "warning"], events.Take(2).Select(Kind));
        Assert.Equal("required", events[0].GetProperty("crewDecision").GetString());
        Assert.Equal(ForgeCrewDecision.DerivedWarning, Code(events[1]));
        Assert.Equal("required", RecordedMode(session));
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task Adopting_with_another_crew_decision_mode_is_refused_and_moves_nothing()
    {
        // Arrange
        var session = SessionAtDryPause(ForgeCrewDecision.Optional);
        using var console = new TestConsole();

        // Act
        var exitCode = await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--adopt", "--crew-decision", "none"], _workspace);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains("was created with --crew-decision optional", console.Stderr, StringComparison.Ordinal);
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "tri", out var reloaded, out _));
        Assert.Equal(ForgeState.Test, reloaded!.State);
        Assert.Equal(session.Document.Status, reloaded.Document.Status);
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task Adopting_a_legacy_session_with_another_mode_than_its_plan_is_refused_and_records_nothing()
    {
        // Arrange
        var session = SessionAtDryPause(mode: null);
        using var console = new TestConsole();

        // Act
        var exitCode = await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--adopt", "--crew-decision", "none"], _workspace);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains("was created with --crew-decision required", console.Stderr, StringComparison.Ordinal);
        Assert.Null(RecordedMode(session));
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task A_legacy_resume_with_another_mode_than_its_plan_is_refused_before_anything_is_recorded()
    {
        // Arrange
        var session = SessionAtDryPause(mode: null);
        using var console = new TestConsole();

        // Act
        var exitCode = await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--crew-decision", "none"], _workspace);

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains("was created with --crew-decision required", console.Stderr, StringComparison.Ordinal);
        Assert.Null(RecordedMode(session));
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task A_valid_resume_after_a_refused_one_still_warns_of_the_derived_mode()
    {
        // Arrange
        var settings = WriteSettings();
        SessionAtDryPause(mode: null);
        using (var refused = new TestConsole())
            await ForgeCommand.DispatchAsync(refused, ["resume", "tri", "--crew-decision", "none"], _workspace);
        using var console = new TestConsole();

        // Act
        await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--dry", "--events", "jsonl", "--settings", settings], _workspace);

        // Assert
        Assert.Contains(Events(console.Stdout), e => Code(e) == ForgeCrewDecision.DerivedWarning);
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task A_legacy_resume_warns_of_its_derived_mode_after_session_started()
    {
        // Arrange
        var settings = WriteSettings();
        SessionAtDryPause(mode: null);
        using var console = new TestConsole();

        // Act
        await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--dry", "--events", "jsonl", "--settings", settings], _workspace);

        // Assert
        var events = Events(console.Stdout);
        var started = events.ToList().FindIndex(e => Kind(e) == "session.started");
        var derived = events.ToList().FindIndex(e => Code(e) == ForgeCrewDecision.DerivedWarning);
        Assert.InRange(started, 0, derived - 1);
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task A_required_crew_refusal_comes_after_session_started_and_closes_the_stream()
    {
        // Arrange
        var settings = WriteSettings();
        SessionAtDryPause(ForgeCrewDecision.Required);
        using var console = new TestConsole();

        // Act
        await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--events", "jsonl", "--settings", settings], _workspace);

        // Assert
        var events = Events(console.Stdout);
        Assert.Equal(["session.started", "error", "session.finished"], events.Select(Kind));
        Assert.Equal(ForgeCrewDecision.UnavailableCode, Code(events[1]));
        Assert.Equal(1, events[2].GetProperty("exitCode").GetInt32());
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task A_dry_run_of_a_required_crew_without_system_one_warns_after_session_started()
    {
        // Arrange
        var settings = WriteSettings();
        SessionAtDryPause(ForgeCrewDecision.Required);
        using var console = new TestConsole();

        // Act
        await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--dry", "--events", "jsonl", "--settings", settings], _workspace);

        // Assert
        var events = Events(console.Stdout).ToList();
        var started = events.FindIndex(e => Kind(e) == "session.started");
        var unavailable = events.FindIndex(e => Code(e) == ForgeCrewDecision.UnavailableCode);
        Assert.InRange(started, 0, unavailable - 1);
    }

    [Fact]
    public async Task A_dry_run_of_a_required_crew_without_system_one_only_warns()
    {
        var settings = WriteSettings();
        SessionAtDryPause(ForgeCrewDecision.Required);
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(console, ["resume", "tri", "--dry", "--events", "jsonl", "--settings", settings], _workspace);

        Assert.Equal(0, exitCode);
        Assert.Contains(ForgeCrewDecision.UnavailableCode, console.Stdout, StringComparison.Ordinal);
    }
}
