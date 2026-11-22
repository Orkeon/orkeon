using System.Text.Json;
using Orkeon.Application.Crew.Execution;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The trial of an <c>optional</c> crew (#431): one pass with System One, one through the relays, under
/// <c>runs/&lt;n&gt;/on/</c> and <c>runs/&lt;n&gt;/off/</c>; the verdict passes when both do and keeps the lower score.
/// </summary>
public sealed class ForgeCrewDecisionTrialTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-trial-decision-" + Guid.NewGuid().ToString("N"));

    private readonly StringWriter _output = new();

    public void Dispose()
    {
        _output.Dispose();
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private static readonly IReadOnlyCollection<string> KnownTools = ForgeDocuments.KnownTools;

    private ForgeEngine Engine(ForgeSession session, IForgeTestBench bench, FakeJudge judge, DecisionCircuit host, ForgeRunObserver? observer = null)
    {
        var events = new ForgeEventWriter(_output, new FakeOrkeonClock());
        observer?.Attach(events, withCalls: true);
        var assistant = new ScriptedAssistant()
            .SubmitsBrief(ForgeDocuments.ValidBrief)
            .SubmitsBlueprint(ForgeCrewDecisionPlanTests.TriageBlueprint);
        var channel = new ScriptedUserChannel().Decides("accept");
        return new(
            session,
            events,
            [
                new BriefStage(assistant, channel, null),
                new BlueprintStage(assistant),
                new RenderStage(),
                new ValidateStage(KnownTools),
                new TestStage(bench, observer ?? new ForgeRunObserver(), host),
                new DiagnoseStage(judge),
                new VerdictStage(false, channel, KnownTools),
            ],
            new FakeOrkeonClock());
    }

    private IReadOnlyList<JsonElement> Events() =>
    [
        .. _output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonElement.Parse(line)),
    ];

    private JsonElement Event(string kind) => Events().Single(e => e.GetProperty("kind").GetString() == kind);

    private static readonly DecisionCircuit HostOff = DecisionCircuit.OffBy(DecisionCircuitCause.NoProvider);

    private async Task<ForgeSession> TryOptionalAsync(
        IForgeTestBench bench, FakeJudge judge, DecisionCircuit host, ForgeBudget? budget = null, ForgeRunObserver? observer = null)
    {
        var session = ForgeSession.Create(_workspace, "tri", budget: budget, crewDecision: ForgeCrewDecision.Optional);
        await Engine(session, bench, judge, host, observer).RunAsync(cancellationToken: TestContext.Current.CancellationToken);
        return session;
    }

    /// <summary>An optional trial whose bench reports a model call and a task per pass, both passes approved.</summary>
    private async Task<ForgeSession> TryObservedOptionalAsync()
    {
        var observer = new ForgeRunObserver();
        return await TryOptionalAsync(
            new FakeCircuitRecordingTestBench { Observer = observer }, new FakeJudge().Approves().Approves(), DecisionCircuit.On,
            observer: observer);
    }

    /// <summary>The trial lines of pass <paramref name="pass"/> (1-based): from its run.started to its run.finished.</summary>
    private IReadOnlyList<JsonElement> PassLines(int pass)
    {
        var events = Events();
        var starts = events.Select((e, i) => (e, i)).Where(p => p.e.GetProperty("kind").GetString() == "run.started").Select(p => p.i).ToList();
        var start = starts[pass - 1];
        var end = events.Select((e, i) => (e, i)).First(p => p.i > start && p.e.GetProperty("kind").GetString() == "run.finished").i;
        return [.. events.Skip(start).Take(end - start + 1)];
    }

    /// <summary>The kinds of <paramref name="lines"/>, the <c>llm.call.*</c> ones folded into one, sorted.</summary>
    private static IEnumerable<string> KindsOf(IReadOnlyList<JsonElement> lines) =>
        lines.Select(static l => l.GetProperty("kind").GetString()!)
            .Select(static k => k.StartsWith("llm.call.", StringComparison.Ordinal) ? "llm.call" : k)
            .Distinct()
            .Order(StringComparer.Ordinal);

    private static string? Text(JsonElement line, string property) =>
        line.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>An optional trial stopped by its budget after its on pass, then resumed on <paramref name="host"/>.</summary>
    private async Task<(ForgeSession Session, FakeCircuitRecordingTestBench Resumed)> ResumeAfterABudgetStopAsync(DecisionCircuit host, FakeJudge judge)
    {
        var first = new FakeCircuitRecordingTestBench { Tokens = 1_500 };
        var session = await TryOptionalAsync(first, new FakeJudge(), DecisionCircuit.On, new ForgeBudget { MaxTokens = 1_000 });
        session.Document.Budget = new ForgeBudget { MaxTokens = 100_000, ConsumedTokens = session.Document.Budget.ConsumedTokens };
        session.SetStatus(ForgeSessionStatus.Active);
        _output.GetStringBuilder().Clear();
        var resumed = new FakeCircuitRecordingTestBench();
        await Engine(session, resumed, judge, host).RunAsync(cancellationToken: TestContext.Current.CancellationToken);
        return (session, resumed);
    }

    private static JsonElement ReadJson(ForgeSession session, string relative) =>
        JsonElement.Parse(File.ReadAllText(Path.Combine(session.Directory, relative)));

    [Fact]
    public async Task An_optional_trial_runs_with_system_one_then_through_the_relays()
    {
        var bench = new FakeCircuitRecordingTestBench();

        await TryOptionalAsync(bench, new FakeJudge().Approves().Approves(), DecisionCircuit.On);

        Assert.Equal(["on", "off"], bench.Circuits.Select(c => ForgeCrewDecision.CircuitName(c!)));
    }

    [Fact]
    public async Task Each_pass_of_an_optional_trial_has_its_own_folder_and_the_run_lists_both()
    {
        var bench = new FakeCircuitRecordingTestBench();

        var session = await TryOptionalAsync(bench, new FakeJudge().Approves().Approves(), DecisionCircuit.On);

        var runDirectory = Path.Combine(session.Directory, TestStage.RunsDirectoryName, "1");
        Assert.True(File.Exists(Path.Combine(runDirectory, "on", "run.json")));
        Assert.True(File.Exists(Path.Combine(runDirectory, "off", "run.json")));
        var run = JsonElement.Parse(await File.ReadAllTextAsync(Path.Combine(runDirectory, "run.json"), TestContext.Current.CancellationToken));
        Assert.Equal(["on", "off"], run.GetProperty("passes").EnumerateArray().Select(p => p.GetProperty("circuit").GetString()));
    }

    [Fact]
    public async Task The_verdict_of_an_optional_trial_keeps_the_lower_score_and_lists_its_passes()
    {
        var bench = new FakeCircuitRecordingTestBench();

        await TryOptionalAsync(bench, new FakeJudge().Approves(0.95).Approves(0.8), DecisionCircuit.On);

        var verdict = Event("verdict.ready");
        Assert.Equal(0.8, verdict.GetProperty("score").GetDouble(), 3);
        Assert.Equal(["on", "off"], verdict.GetProperty("passes").EnumerateArray().Select(p => p.GetProperty("circuit").GetString()));
    }

    [Fact]
    public async Task An_optional_trial_fails_when_one_of_its_passes_fails()
    {
        var bench = new FakeCircuitRecordingTestBench();

        await TryOptionalAsync(bench, new FakeJudge().Approves().Rejects(), DecisionCircuit.On);

        var verdict = Event("verdict.ready");
        Assert.False(verdict.GetProperty("passing").GetBoolean());
    }

    [Fact]
    public async Task A_finding_of_an_optional_trial_names_the_circuit_of_its_pass()
    {
        var bench = new FakeCircuitRecordingTestBench();

        await TryOptionalAsync(bench, new FakeJudge().Approves().Rejects(), DecisionCircuit.On);

        var finding = Event("verdict.ready").GetProperty("findings").EnumerateArray().First(f => f.GetProperty("id").GetString() != ForgeCrewDecision.PassOnSkippedFinding);
        Assert.Equal("off", finding.GetProperty("circuit").GetString());
    }

    [Fact]
    public async Task An_optional_trial_without_a_decision_model_runs_through_the_relays_only()
    {
        var bench = new FakeCircuitRecordingTestBench();

        await TryOptionalAsync(bench, new FakeJudge().Approves(), HostOff);

        Assert.Equal(["off"], bench.Circuits.Select(c => ForgeCrewDecision.CircuitName(c!)));
    }

    [Fact]
    public async Task An_optional_trial_without_a_decision_model_warns_with_the_cause()
    {
        var bench = new FakeCircuitRecordingTestBench();

        await TryOptionalAsync(bench, new FakeJudge().Approves(), HostOff);

        var warning = Events().Single(e => e.GetProperty("kind").GetString() == "warning"
            && e.GetProperty("code").GetString() == ForgeCrewDecision.PassSkippedWarning);
        Assert.Equal("no-provider", warning.GetProperty("cause").GetString());
        Assert.False(warning.TryGetProperty("detail", out _));
    }

    /// <summary>#467 — the off circuit's sentence rides the skipped-pass warning and the off pass's run.started.</summary>
    [Fact]
    [Trait("BR", "CH-467")]
    public async Task An_optional_trial_without_a_key_carries_the_circuits_detail_on_the_warning_and_run_started()
    {
        const string Sentence = "no Liquid API key: set the environment variable LIQUID_API_KEY, or Orkeon:Liquid:ApiKey";
        var host = DecisionCircuit.OffBy(DecisionCircuitCause.NoKey) with { Detail = Sentence };

        await TryOptionalAsync(new FakeCircuitRecordingTestBench(), new FakeJudge().Approves(), host);

        var warning = Events().Single(e => e.GetProperty("kind").GetString() == "warning"
            && e.GetProperty("code").GetString() == ForgeCrewDecision.PassSkippedWarning);
        var started = Events().Single(e => e.GetProperty("kind").GetString() == "run.started");
        Assert.Equal((Sentence, "no-key", Sentence), (warning.GetProperty("detail").GetString(), started.GetProperty("cause").GetString(), started.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task An_optional_trial_without_its_on_pass_adds_an_info_finding()
    {
        var bench = new FakeCircuitRecordingTestBench();

        await TryOptionalAsync(bench, new FakeJudge().Approves(), HostOff);

        var finding = Event("verdict.ready").GetProperty("findings").EnumerateArray()
            .Single(f => f.GetProperty("id").GetString() == ForgeCrewDecision.PassOnSkippedFinding);
        Assert.Equal("info", finding.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task A_budget_used_up_by_the_on_pass_ends_the_session_before_the_off_pass()
    {
        var bench = new FakeCircuitRecordingTestBench { Tokens = 1_500 };

        var session = await TryOptionalAsync(bench, new FakeJudge().Approves().Approves(), DecisionCircuit.On, new ForgeBudget { MaxTokens = 1_000 });

        Assert.Equal(ForgeSessionStatus.BudgetExhausted, session.Status);
        Assert.Equal(["on"], bench.Circuits.Select(c => ForgeCrewDecision.CircuitName(c!)));
    }

    [Fact]
    public async Task A_resume_after_a_budget_stop_between_passes_runs_the_off_pass_only()
    {
        var first = new FakeCircuitRecordingTestBench { Tokens = 1_500 };
        var session = await TryOptionalAsync(first, new FakeJudge(), DecisionCircuit.On, new ForgeBudget { MaxTokens = 1_000 });
        session.Document.Budget = new ForgeBudget { MaxTokens = 100_000, ConsumedTokens = session.Document.Budget.ConsumedTokens };
        session.SetStatus(ForgeSessionStatus.Active);
        var resumed = new FakeCircuitRecordingTestBench();

        await Engine(session, resumed, new FakeJudge().Approves().Approves(), DecisionCircuit.On)
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["off"], resumed.Circuits.Select(c => ForgeCrewDecision.CircuitName(c!)));
    }

    // --- the trial lines (#431) ------------------------------------------------------------------

    [Fact]
    public async Task Every_line_of_the_off_pass_carries_the_off_circuit_and_its_cause()
    {
        await TryObservedOptionalAsync();

        var lines = PassLines(2);
        Assert.Equal(["llm.call", "run.finished", "run.started", "task.completed"], KindsOf(lines));
        Assert.All(lines, line => Assert.Equal(("off", "trial"), (Text(line, "circuit"), Text(line, "cause"))));
    }

    [Fact]
    public async Task Every_line_of_the_on_pass_carries_the_on_circuit_and_no_cause()
    {
        await TryObservedOptionalAsync();

        var lines = PassLines(1);
        Assert.Equal(["llm.call", "run.finished", "run.started", "task.completed"], KindsOf(lines));
        Assert.All(lines, line =>
        {
            Assert.Equal("on", Text(line, "circuit"));
            Assert.Null(Text(line, "cause"));
        });
    }

    [Fact]
    public async Task Run_started_numbers_each_pass_of_an_optional_trial()
    {
        await TryOptionalAsync(new FakeCircuitRecordingTestBench(), new FakeJudge().Approves().Approves(), DecisionCircuit.On);

        var started = Events().Where(e => e.GetProperty("kind").GetString() == "run.started")
            .Select(e => (e.GetProperty("passes").GetInt32(), e.GetProperty("pass").GetInt32()));
        Assert.Equal([(2, 1), (2, 2)], started);
    }

    // --- the run files (#431) --------------------------------------------------------------------

    [Fact]
    public async Task Last_run_json_lists_both_passes_of_an_optional_trial()
    {
        var session = await TryOptionalAsync(new FakeCircuitRecordingTestBench(), new FakeJudge().Approves().Approves(), DecisionCircuit.On);

        var lastRun = ReadJson(session, TestStage.LastRunFileName);

        Assert.Equal(["on", "off"], lastRun.GetProperty("passes").EnumerateArray().Select(p => p.GetProperty("circuit").GetString()));
    }

    [Fact]
    public async Task A_pass_of_the_run_file_names_its_output_file_under_output()
    {
        var session = await TryOptionalAsync(new FakeCircuitRecordingTestBench(), new FakeJudge().Approves().Approves(), DecisionCircuit.On);

        var run = ReadJson(session, Path.Combine(TestStage.RunsDirectoryName, "1", "run.json"));
        Assert.Equal(["runs/1/on/output.md", "runs/1/off/output.md"], run.GetProperty("passes").EnumerateArray().Select(p => p.GetProperty("output").GetString()));
    }

    [Fact]
    public async Task A_pass_of_verdict_ready_carries_its_figures_flat()
    {
        await TryOptionalAsync(new FakeCircuitRecordingTestBench { Tokens = 700, DurationMs = 90 }, new FakeJudge().Approves().Approves(), DecisionCircuit.On);

        var pass = Event("verdict.ready").GetProperty("passes")[0];
        Assert.Equal((700L, 90L), (pass.GetProperty("tokens").GetInt64(), pass.GetProperty("durationMs").GetInt64()));
    }

    [Fact]
    public async Task An_optional_trial_with_the_circuit_off_by_the_settings_warns_with_the_settings_cause()
    {
        await TryOptionalAsync(new FakeCircuitRecordingTestBench(), new FakeJudge().Approves(), DecisionCircuit.OffBy(DecisionCircuitCause.Settings));

        var warning = Events().Single(e => e.GetProperty("kind").GetString() == "warning"
            && e.GetProperty("code").GetString() == ForgeCrewDecision.PassSkippedWarning);
        Assert.Equal("settings", warning.GetProperty("cause").GetString());
    }

    // --- a resume once the decision model is gone (#431) -----------------------------------------

    [Fact]
    public async Task A_resume_after_a_budget_stop_without_a_decision_model_runs_the_off_pass_only()
    {
        var (_, resumed) = await ResumeAfterABudgetStopAsync(HostOff, new FakeJudge().Approves().Approves());

        var circuits = resumed.Circuits.Select(c => ForgeCrewDecision.CircuitName(c!));
        Assert.Equal(["off"], circuits);
    }

    [Fact]
    public async Task A_resume_after_a_budget_stop_without_a_decision_model_keeps_the_on_pass_in_its_run()
    {
        var (session, _) = await ResumeAfterABudgetStopAsync(HostOff, new FakeJudge().Approves().Approves());

        var run = ReadJson(session, Path.Combine(TestStage.RunsDirectoryName, "1", "run.json"));
        Assert.Equal([("on", null), ("off", "no-provider")], run.GetProperty("passes").EnumerateArray().Select(p => (Text(p, "circuit"), Text(p, "cause"))));
    }

    [Fact]
    public async Task A_resume_after_a_budget_stop_without_a_decision_model_reports_no_skipped_on_pass()
    {
        await ResumeAfterABudgetStopAsync(HostOff, new FakeJudge().Approves().Approves());

        var findings = Event("verdict.ready").GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("id").GetString());
        Assert.DoesNotContain(ForgeCrewDecision.PassOnSkippedFinding, findings);
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task A_resume_after_a_budget_stop_without_a_decision_model_warns_of_no_skipped_pass()
    {
        // Arrange
        var judge = new FakeJudge().Approves().Approves();

        // Act
        await ResumeAfterABudgetStopAsync(HostOff, judge);

        // Assert
        Assert.DoesNotContain(Events(), e => e.GetProperty("kind").GetString() == "warning"
            && e.GetProperty("code").GetString() == ForgeCrewDecision.PassSkippedWarning);
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public async Task A_pass_whose_snapshot_fails_leaves_no_run_file_a_resume_would_keep()
    {
        // Arrange: the pass writes a deliverable under /output, and a file takes the place of its snapshot.
        var bench = new FakeCircuitRecordingTestBench
        {
            DuringRun = session =>
            {
                var output = new ForgeFolder { Path = "/output", Role = ForgeFolders.OutputRole, Purpose = "p" };
                session.SaveArtifact(ForgeFolders.FileName, new ForgeFolderList { Folders = [output] });
                var written = ForgeFolders.SessionFolder(session, output);
                Directory.CreateDirectory(written);
                File.WriteAllText(Path.Combine(written, "report.md"), "the report");
                var passDirectory = Path.Combine(session.Directory, TestStage.RunsDirectoryName, "1", "on");
                Directory.CreateDirectory(passDirectory);
                File.WriteAllText(Path.Combine(passDirectory, ForgeFolders.SessionDirectoryName), "in the way");
            },
        };
        var session = ForgeSession.Create(_workspace, "tri", crewDecision: ForgeCrewDecision.Optional);

        // Act
        var result = await Engine(session, bench, new FakeJudge().Approves().Approves(), DecisionCircuit.On)
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the on pass ran, its snapshot is what failed, and it left no run file behind.
        Assert.Equal(["on"], bench.Circuits.Select(c => ForgeCrewDecision.CircuitName(c!)));
        Assert.Equal(ForgeEngineOutcome.Failed, result.Outcome);
        var error = Event("error");
        Assert.Equal(ForgeEngine.CodeEngineCrashed, error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("terminal").GetBoolean());
        Assert.Contains(ForgeFolders.SessionDirectoryName, error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(session.Directory, TestStage.RunsDirectoryName, "1", "on", "run.json")));
    }

    [Fact]
    public async Task A_required_trial_runs_one_pass_on_the_hosts_circuit()
    {
        var bench = new FakeCircuitRecordingTestBench();
        var session = ForgeSession.Create(_workspace, "tri", crewDecision: ForgeCrewDecision.Required);

        await Engine(session, bench, new FakeJudge().Approves(), DecisionCircuit.On)
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([null], bench.Circuits);
    }
}
