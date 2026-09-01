using System.Text.Json;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>One stage scripted by the test: records its visits, returns what it was told to.</summary>
internal sealed class ScriptedRunner : IForgeStageRunner
{
    private readonly Func<ForgeSession, ForgeStageOutcome> _outcome;

    public ScriptedRunner(ForgeState stage, ForgeTrigger trigger, long tokens = 0)
        : this(stage, _ => new ForgeStageOutcome
        {
            Trigger = trigger,
            // Everything on the ascending side: what this fixture meters is the total.
            Usage = new ForgeUsageSnapshot(tokens, 0, 0),
        }) { }

    public ScriptedRunner(ForgeState stage, Func<ForgeSession, ForgeStageOutcome> outcome)
    {
        Stage = stage;
        _outcome = outcome;
    }

    public ForgeState Stage { get; }

    /// <summary>How many times the engine entered this stage.</summary>
    public int Visits { get; private set; }

    public Task<ForgeStageOutcome> RunAsync(
        ForgeSession session, ForgeEventWriter events, CancellationToken cancellationToken)
    {
        Visits++;
        return Task.FromResult(_outcome(session));
    }
}

/// <summary>
/// The deterministic spine (SPEC-ORKEON-FORGE §3.3): stages run one at a time, only their
/// triggers move the machine, the budget arbitrates loop-backs, every step is saved, and
/// the ends of the cycle map to the protocol's statuses and the CLI's exit codes.
/// </summary>
public sealed class ForgeEngineTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-engine-" + Guid.NewGuid().ToString("N"));

    private readonly StringWriter _output = new();

    public void Dispose()
    {
        _output.Dispose();
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private static IReadOnlyList<ScriptedRunner> NominalRunners() =>
    [
        new(ForgeState.Brief, ForgeTrigger.BriefSubmitted),
        new(ForgeState.Blueprint, ForgeTrigger.BlueprintSubmitted),
        new(ForgeState.Render, ForgeTrigger.Rendered),
        new(ForgeState.Validate, ForgeTrigger.Validated),
        new(ForgeState.Test, ForgeTrigger.TestCompleted),
        new(ForgeState.Diagnose, ForgeTrigger.Diagnosed),
        new(ForgeState.Verdict, ForgeTrigger.Accepted),
    ];

    private ForgeEngine Engine(ForgeSession session, IEnumerable<IForgeStageRunner> runners) =>
        new(session, new ForgeEventWriter(_output, new FakeOrkeonClock()), runners, new FakeOrkeonClock());

    private IReadOnlyList<JsonElement> Events() =>
    [
        .. _output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement),
    ];

    [Fact]
    public async Task The_nominal_cycle_ends_ready_with_the_crew_waiting_for_promotion()
    {
        var session = ForgeSession.Create(_workspace, "demo");
        var result = await Engine(session, NominalRunners())
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Ready, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ForgeState.Ready, session.State);
        Assert.Equal(ForgeSessionStatus.Ready, session.Status);

        var events = Events();
        Assert.Equal("session.started", events[0].GetProperty("kind").GetString());
        // Which build answered. Without it a client cannot tell an engine that reports
        // nothing from one too old to report it — two states that look identical on screen.
        Assert.False(string.IsNullOrWhiteSpace(events[0].GetProperty("engine").GetString()));
        Assert.Equal(
            ["brief", "blueprint", "render", "validate", "test", "diagnose", "verdict"],
            events.Where(e => e.GetProperty("kind").GetString() == "stage.entered")
                .Select(e => e.GetProperty("stage").GetString()));
        Assert.Equal("ready", events[^1].GetProperty("status").GetString());

        // Seven transitions, all in the append-only history.
        var history = await File.ReadAllLinesAsync(
            Path.Combine(session.Directory, ForgeSession.HistoryFileName),
            TestContext.Current.CancellationToken);
        Assert.Equal(7, history.Length);
    }

    [Fact]
    public async Task A_refine_beyond_the_iteration_budget_stops_hard_and_stays_resumable()
    {
        var session = ForgeSession.Create(
            _workspace, "demo", budget: new ForgeBudget { MaxIterations = 1 });
        var runners = NominalRunners().ToList();
        runners[6] = new ScriptedRunner(ForgeState.Verdict, ForgeTrigger.RefineRequested);

        var result = await Engine(session, runners)
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.BudgetExhausted, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ForgeSessionStatus.BudgetExhausted, session.Status);

        // The refused cycle cost nothing: the session is still at the verdict, resumable.
        Assert.Equal(ForgeState.Verdict, session.State);

        var events = Events();
        var error = Assert.Single(events, e => e.GetProperty("kind").GetString() == "error");
        Assert.Equal(ForgeEngine.CodeBudgetExhausted, error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("recoverable").GetBoolean());
        Assert.Equal("abandoned", events[^1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Within_budget_a_refine_goes_back_through_the_blueprint()
    {
        var session = ForgeSession.Create(
            _workspace, "demo", budget: new ForgeBudget { MaxIterations = 2 });
        var refined = false;
        var runners = NominalRunners().ToList();
        runners[6] = new ScriptedRunner(ForgeState.Verdict, _ =>
        {
            var trigger = refined ? ForgeTrigger.Accepted : ForgeTrigger.RefineRequested;
            refined = true;
            return new ForgeStageOutcome { Trigger = trigger };
        });

        var result = await Engine(session, runners)
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Ready, result.Outcome);
        Assert.Equal(2, session.Document.Budget.ConsumedIterations);
        Assert.Equal(2, session.Document.Iteration);

        // The blueprint ran twice: once per cycle.
        Assert.Equal(2, ((ScriptedRunner)runners[1]).Visits);
    }

    [Fact]
    public async Task A_resumed_session_starts_at_its_saved_stage()
    {
        var session = ForgeSession.Create(_workspace, "demo");
        session.SetState(ForgeState.Test);
        session.Save();

        var runners = NominalRunners();
        var result = await Engine(session, runners)
            .RunAsync(resumed: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Ready, result.Outcome);
        Assert.Equal(0, ((ScriptedRunner)runners[0]).Visits);   // Brief never re-ran
        Assert.Equal(1, ((ScriptedRunner)runners[4]).Visits);   // Test did

        var started = Events()[0];
        Assert.True(started.GetProperty("resumed").GetBoolean());
    }

    [Fact]
    public async Task A_stage_without_a_runner_fails_with_the_engine_incomplete_code()
    {
        var session = ForgeSession.Create(_workspace, "demo");

        var result = await Engine(session, [new ScriptedRunner(ForgeState.Brief, ForgeTrigger.BriefSubmitted)])
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Failed, result.Outcome);
        Assert.Equal(2, result.ExitCode);
        Assert.Equal(ForgeSessionStatus.Failed, session.Status);
        Assert.Contains(ForgeEngine.CodeEngineIncomplete, session.Document.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_illegal_trigger_is_an_engine_failure_not_a_silent_move()
    {
        var session = ForgeSession.Create(_workspace, "demo");

        var result = await Engine(session, [new ScriptedRunner(ForgeState.Brief, ForgeTrigger.Promote)])
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Failed, result.Outcome);
        Assert.Contains(ForgeEngine.CodeInvalidTransition, session.Document.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Abandoning_at_the_verdict_ends_clean_with_exit_zero()
    {
        var session = ForgeSession.Create(_workspace, "demo");
        var runners = NominalRunners().ToList();
        runners[6] = new ScriptedRunner(ForgeState.Verdict, ForgeTrigger.Abandon);

        var result = await Engine(session, runners)
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Abandoned, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ForgeSessionStatus.Abandoned, session.Status);
        Assert.Equal("abandoned", Events()[^1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Stage_token_consumption_is_charged_and_can_exhaust_the_budget()
    {
        var session = ForgeSession.Create(
            _workspace, "demo", budget: new ForgeBudget { MaxTokens = 100 });
        var runners = new List<IForgeStageRunner>
        {
            new ScriptedRunner(ForgeState.Brief, ForgeTrigger.BriefSubmitted, tokens: 120),
            new ScriptedRunner(ForgeState.Blueprint, ForgeTrigger.BlueprintSubmitted),
        };

        var result = await Engine(session, runners)
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.BudgetExhausted, result.Outcome);
        Assert.Equal(120, session.Document.Budget.ConsumedTokens);
        // The brief's trigger still moved the machine — only the *next* stage was refused.
        Assert.Equal(ForgeState.Blueprint, session.State);
        Assert.Equal(0, ((ScriptedRunner)runners[1]).Visits);
    }

    /// <summary>
    /// The client cannot show what the wire does not carry. The split reached the engine and
    /// stopped there: cost.updated said «tokens» and nothing else, so Studio's ↑/↓ meter had
    /// no source and stood empty through a whole session.
    /// </summary>
    [Fact]
    public async Task What_a_stage_spent_reaches_the_wire_split_by_direction()
    {
        var session = ForgeSession.Create(_workspace, "demo");
        var runners = new List<IForgeStageRunner>
        {
            new ScriptedRunner(ForgeState.Brief, _ => new ForgeStageOutcome
            {
                Trigger = ForgeTrigger.BriefSubmitted,
                Usage = new ForgeUsageSnapshot(900, 120, 0),
            }),
            new ScriptedRunner(ForgeState.Blueprint, _ => new ForgeStageOutcome
            {
                Trigger = ForgeTrigger.Fail,
                FailureCode = "STOP",
                Detail = "enough for this test",
            }),
        };

        await Engine(session, runners).RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        var cost = Events().Single(e => e.GetProperty("kind").GetString() == "cost.updated");
        Assert.Equal(1020, cost.GetProperty("tokens").GetInt64());
        Assert.Equal(900, cost.GetProperty("promptTokens").GetInt64());
        Assert.Equal(120, cost.GetProperty("completionTokens").GetInt64());
        // Nothing was approximated here, and the event says zero rather than staying silent:
        // «no estimate» is a fact the client needs to NOT mark the figures with a «≈».
        Assert.Equal(0, cost.GetProperty("estimatedTokens").GetInt64());
    }
}
