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

    private ForgeEngine Engine(ForgeSession session, IForgeTestBench bench, FakeJudge judge, DecisionCircuit host)
    {
        var assistant = new ScriptedAssistant()
            .SubmitsBrief(ForgeDocuments.ValidBrief)
            .SubmitsBlueprint(ForgeCrewDecisionPlanTests.TriageBlueprint);
        var channel = new ScriptedUserChannel().Decides("accept");
        return new(
            session,
            new ForgeEventWriter(_output, new FakeOrkeonClock()),
            [
                new BriefStage(assistant, channel, null),
                new BlueprintStage(assistant),
                new RenderStage(),
                new ValidateStage(KnownTools),
                new TestStage(bench, new ForgeRunObserver(), host),
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

    private async Task<ForgeSession> TryOptionalAsync(IForgeTestBench bench, FakeJudge judge, DecisionCircuit host, ForgeBudget? budget = null)
    {
        var session = ForgeSession.Create(_workspace, "tri", budget: budget, crewDecision: ForgeCrewDecision.Optional);
        await Engine(session, bench, judge, host).RunAsync(cancellationToken: TestContext.Current.CancellationToken);
        return session;
    }

    [Fact]
    public async Task An_optional_trial_runs_with_system_one_then_through_the_relays()
    {
        var bench = new CircuitRecordingTestBench();

        await TryOptionalAsync(bench, new FakeJudge().Approves().Approves(), DecisionCircuit.On);

        Assert.Equal(["on", "off"], bench.Circuits.Select(c => ForgeCrewDecision.CircuitName(c!)));
    }

    [Fact]
    public async Task Each_pass_of_an_optional_trial_has_its_own_folder_and_the_run_lists_both()
    {
        var bench = new CircuitRecordingTestBench();

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
        var bench = new CircuitRecordingTestBench();

        await TryOptionalAsync(bench, new FakeJudge().Approves(0.95).Approves(0.8), DecisionCircuit.On);

        var verdict = Event("verdict.ready");
        Assert.Equal(0.8, verdict.GetProperty("score").GetDouble(), 3);
        Assert.Equal(["on", "off"], verdict.GetProperty("passes").EnumerateArray().Select(p => p.GetProperty("circuit").GetString()));
    }

    [Fact]
    public async Task An_optional_trial_fails_when_one_of_its_passes_fails()
    {
        var bench = new CircuitRecordingTestBench();

        await TryOptionalAsync(bench, new FakeJudge().Approves().Rejects(), DecisionCircuit.On);

        var verdict = Event("verdict.ready");
        Assert.False(verdict.GetProperty("passing").GetBoolean());
    }

    [Fact]
    public async Task A_finding_of_an_optional_trial_names_the_circuit_of_its_pass()
    {
        var bench = new CircuitRecordingTestBench();

        await TryOptionalAsync(bench, new FakeJudge().Approves().Rejects(), DecisionCircuit.On);

        var finding = Event("verdict.ready").GetProperty("findings").EnumerateArray().First(f => f.GetProperty("id").GetString() != ForgeCrewDecision.PassOnSkippedFinding);
        Assert.Equal("off", finding.GetProperty("circuit").GetString());
    }

    [Fact]
    public async Task An_optional_trial_without_a_decision_model_runs_through_the_relays_only()
    {
        var bench = new CircuitRecordingTestBench();

        await TryOptionalAsync(bench, new FakeJudge().Approves(), HostOff);

        Assert.Equal(["off"], bench.Circuits.Select(c => ForgeCrewDecision.CircuitName(c!)));
    }

    [Fact]
    public async Task An_optional_trial_without_a_decision_model_warns_with_the_cause()
    {
        var bench = new CircuitRecordingTestBench();

        await TryOptionalAsync(bench, new FakeJudge().Approves(), HostOff);

        var warning = Events().Single(e => e.GetProperty("kind").GetString() == "warning"
            && e.GetProperty("code").GetString() == ForgeCrewDecision.PassSkippedWarning);
        Assert.Equal("no-provider", warning.GetProperty("cause").GetString());
    }

    [Fact]
    public async Task An_optional_trial_without_its_on_pass_adds_an_info_finding()
    {
        var bench = new CircuitRecordingTestBench();

        await TryOptionalAsync(bench, new FakeJudge().Approves(), HostOff);

        var finding = Event("verdict.ready").GetProperty("findings").EnumerateArray()
            .Single(f => f.GetProperty("id").GetString() == ForgeCrewDecision.PassOnSkippedFinding);
        Assert.Equal("info", finding.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task A_budget_used_up_by_the_on_pass_ends_the_session_before_the_off_pass()
    {
        var bench = new CircuitRecordingTestBench { Tokens = 1_500 };

        var session = await TryOptionalAsync(bench, new FakeJudge().Approves().Approves(), DecisionCircuit.On, new ForgeBudget { MaxTokens = 1_000 });

        Assert.Equal(ForgeSessionStatus.BudgetExhausted, session.Status);
        Assert.Equal(["on"], bench.Circuits.Select(c => ForgeCrewDecision.CircuitName(c!)));
    }

    [Fact]
    public async Task A_resume_after_a_budget_stop_between_passes_runs_the_off_pass_only()
    {
        var first = new CircuitRecordingTestBench { Tokens = 1_500 };
        var session = await TryOptionalAsync(first, new FakeJudge(), DecisionCircuit.On, new ForgeBudget { MaxTokens = 1_000 });
        session.Document.Budget = new ForgeBudget { MaxTokens = 100_000, ConsumedTokens = session.Document.Budget.ConsumedTokens };
        session.SetStatus(ForgeSessionStatus.Active);
        var resumed = new CircuitRecordingTestBench();

        await Engine(session, resumed, new FakeJudge().Approves().Approves(), DecisionCircuit.On)
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["off"], resumed.Circuits.Select(c => ForgeCrewDecision.CircuitName(c!)));
    }

    [Fact]
    public async Task A_required_trial_runs_one_pass_on_the_hosts_circuit()
    {
        var bench = new CircuitRecordingTestBench();
        var session = ForgeSession.Create(_workspace, "tri", crewDecision: ForgeCrewDecision.Required);

        await Engine(session, bench, new FakeJudge().Approves(), DecisionCircuit.On)
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([null], bench.Circuits);
    }
}
