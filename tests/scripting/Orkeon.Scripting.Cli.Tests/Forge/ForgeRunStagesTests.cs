using System.Text.Json;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>Scripted <see cref="IForgeTestBench"/>: a queue of runs, or a crash.</summary>
internal sealed class FakeTestBench : IForgeTestBench
{
    private readonly Queue<ForgeTestRun> _runs = new();

    /// <summary>How many times the bench executed.</summary>
    public int Executions { get; private set; }

    /// <summary>Queues a successful run.</summary>
    public FakeTestBench Succeeds(string output = "Le résumé, sources citées.", long tokens = 500)
    {
        _runs.Enqueue(new ForgeTestRun { Success = true, Output = output, TaskCount = 2, DurationMs = 40, Tokens = tokens });
        return this;
    }

    /// <summary>Queues a failed run.</summary>
    public FakeTestBench Fails(string error = "Crew execution failed: boom")
    {
        _runs.Enqueue(new ForgeTestRun { Success = false, Output = error, Error = error });
        return this;
    }

    /// <inheritdoc />
    public Task<ForgeTestRun> ExecuteAsync(ForgeSession session, int runNumber, CancellationToken cancellationToken)
    {
        Executions++;
        var run = _runs.Count > 0 ? _runs.Dequeue() : new ForgeTestRun { Success = true, Output = "(default)" };
        return Task.FromResult(run with { Run = runNumber });
    }
}

/// <summary>Scripted <see cref="IForgeJudge"/>: a queue of verdicts (null = judge unavailable).</summary>
internal sealed class FakeJudge : IForgeJudge
{
    private readonly Queue<ForgeVerdict?> _verdicts = new();

    /// <summary>How many times the judge ran.</summary>
    public int Calls { get; private set; }

    /// <summary>Queues a conforming verdict.</summary>
    public FakeJudge Approves(double score = 0.9)
    {
        _verdicts.Enqueue(new ForgeVerdict { Score = score, Passing = true, Judge = ForgeVerdict.JudgeLlm });
        return this;
    }

    /// <summary>Queues a non-conforming verdict with one finding and one suggestion.</summary>
    public FakeJudge Rejects(string statement = "Les prix ne sont pas comparés", string acceptance = "A1")
    {
        _verdicts.Enqueue(new ForgeVerdict
        {
            Score = 0.4,
            Judge = ForgeVerdict.JudgeLlm,
            Findings = [new ForgeFinding { Id = "F1", Severity = "major", Acceptance = acceptance, Statement = statement }],
            Suggestions = [new ForgeSuggestion { Target = "agent:redacteur", Change = "comparer aux prix d'hier", Reason = "critère A1" }],
        });
        return this;
    }

    /// <summary>Tokens each call reports, charged to the session budget.</summary>
    public long TokensPerCall { get; set; }

    /// <inheritdoc />
    public Task<ForgeJudgement> JudgeAsync(ForgeBrief brief, string output, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new ForgeJudgement(
            _verdicts.Count > 0 ? _verdicts.Dequeue() : null, TokensPerCall));
    }
}

/// <summary>
/// The run stages under the real engine (SPEC-ORKEON-FORGE §9-§10): the full cycle to
/// Ready, the refine loop fed by the diagnosis, the arbitration, and the degraded verdict
/// that says it is one. Scripted seams everywhere — no LLM, no crew, no process.
/// </summary>
public sealed class ForgeRunStagesTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-run-" + Guid.NewGuid().ToString("N"));

    private readonly StringWriter _output = new();

    public void Dispose()
    {
        _output.Dispose();
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private ForgeEngine Engine(ForgeSession session, params IForgeStageRunner[] runners) =>
        new(session, new ForgeEventWriter(_output, new FakeOrkeonClock()), runners, new FakeOrkeonClock());

    private IReadOnlyList<JsonElement> Events() =>
    [
        .. _output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement),
    ];

    private IReadOnlyList<string> Kinds() => [.. Events().Select(e => e.GetProperty("kind").GetString()!)];

    private static IForgeStageRunner[] FullRunners(
        ScriptedAssistant assistant,
        ScriptedUserChannel channel,
        FakeTestBench bench,
        FakeJudge judge,
        bool auto = false,
        string? need = null) =>
    [
        new BriefStage(assistant, channel, need),
        new BlueprintStage(assistant),
        new RenderStage(),
        new ValidateStage(ForgeDocuments.KnownTools),
        new TestStage(bench),
        new DiagnoseStage(judge),
        new VerdictStage(auto, channel),
    ];

    private static ScriptedAssistant HappyAssistant() => new ScriptedAssistant()
        .SubmitsBrief(ForgeDocuments.ValidBrief)
        .SubmitsBlueprint(ForgeDocuments.ValidBlueprint);

    [Fact]
    public async Task The_full_cycle_runs_to_ready_with_the_run_snapshotted()
    {
        var bench = new FakeTestBench().Succeeds(tokens: 500);
        var judge = new FakeJudge().Approves();
        var session = ForgeSession.Create(_workspace, "veille");

        var result = await Engine(session, FullRunners(HappyAssistant(), new ScriptedUserChannel(), bench, judge))
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Ready, result.Outcome);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ForgeSessionStatus.Ready, session.Status);

        // The run is snapshotted where the spec's session layout says (§4.1).
        Assert.True(File.Exists(Path.Combine(session.Directory, "runs", "1", "output.md")));
        Assert.True(File.Exists(Path.Combine(session.Directory, "runs", "1", "run.json")));
        Assert.True(File.Exists(Path.Combine(session.Directory, "runs", "1", "verdict.json")));

        // The run's tokens were charged to the session budget.
        Assert.Equal(500, session.Document.Budget.ConsumedTokens);

        Assert.Equal(
            ["session.started", "stage.entered", "brief.ready", "stage.entered", "blueprint.ready",
             "stage.entered", "file.written", "file.written", "file.written", "file.written", "file.written",
             "stage.entered", "validation.result", "stage.entered", "run.started", "run.finished",
             "cost.updated", "stage.entered", "verdict.ready", "stage.entered", "session.finished"],
            Kinds());
        Assert.Equal("ready", Events()[^1].GetProperty("status").GetString());

        // The meter is cumulative and honest about the absence of a ceiling.
        var cost = Events().Single(e => e.GetProperty("kind").GetString() == "cost.updated");
        Assert.Equal(500, cost.GetProperty("tokens").GetInt64());
        Assert.Equal(JsonValueKind.Null, cost.GetProperty("budgetRemaining").ValueKind);
    }

    [Fact]
    public async Task Auto_mode_refines_on_a_non_conforming_verdict_and_feeds_the_diagnosis_back()
    {
        var assistant = HappyAssistant().SubmitsBlueprint(ForgeDocuments.ValidBlueprint);
        var bench = new FakeTestBench().Succeeds().Succeeds();
        var judge = new FakeJudge().Rejects().Approves();
        var session = ForgeSession.Create(_workspace, "veille");

        var result = await Engine(session, FullRunners(assistant, new ScriptedUserChannel(), bench, judge, auto: true))
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Ready, result.Outcome);
        Assert.Equal(2, bench.Executions);
        Assert.Equal(2, session.Document.Budget.ConsumedIterations);
        Assert.True(Directory.Exists(Path.Combine(session.Directory, "runs", "2")));

        // The refine turn carried the finding and the suggestion, verbatim.
        var refine = assistant.Requests[^1];
        Assert.Equal(ForgeAssistantPhase.Blueprint, refine.Phase);
        Assert.Contains(refine.Errors!, e => e.Contains("Les prix ne sont pas comparés", StringComparison.Ordinal));
        Assert.Contains(refine.Errors!, e => e.Contains("agent:redacteur", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_user_may_accept_a_non_conforming_result_on_sight()
    {
        var bench = new FakeTestBench().Succeeds();
        var judge = new FakeJudge().Rejects();
        var channel = new ScriptedUserChannel().Decides("accept");
        var session = ForgeSession.Create(_workspace, "veille");

        var result = await Engine(session, FullRunners(HappyAssistant(), channel, bench, judge))
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Ready, result.Outcome);
        Assert.Contains("decision.needed", Kinds());
    }

    [Fact]
    public async Task The_user_may_abort_at_the_arbitration()
    {
        var bench = new FakeTestBench().Succeeds();
        var judge = new FakeJudge().Rejects();
        var channel = new ScriptedUserChannel().Decides("abort");
        var session = ForgeSession.Create(_workspace, "veille");

        var result = await Engine(session, FullRunners(HappyAssistant(), channel, bench, judge))
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Abandoned, result.Outcome);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Without_a_judge_the_verdict_is_degraded_and_says_so()
    {
        // No promised deliverable here: a mechanically clean run must pass on its own.
        var noDeliverable = ForgeDocuments.ValidBlueprint
            .Replace(", \"deliverable\": \"/output/resume.md\"", "", StringComparison.Ordinal);
        var assistant = new ScriptedAssistant()
            .SubmitsBrief(ForgeDocuments.ValidBrief)
            .SubmitsBlueprint(noDeliverable);
        var bench = new FakeTestBench().Succeeds();
        var judge = new FakeJudge();   // never queues anything: unavailable
        var session = ForgeSession.Create(_workspace, "veille");

        var result = await Engine(session, FullRunners(assistant, new ScriptedUserChannel(), bench, judge))
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Mechanically clean run → passing, at the threshold, announced as deterministic.
        Assert.Equal(ForgeEngineOutcome.Ready, result.Outcome);
        var verdict = Events().Single(e => e.GetProperty("kind").GetString() == "verdict.ready");
        Assert.Equal("deterministic", verdict.GetProperty("judge").GetString());
        Assert.Equal(ForgeVerdict.PassingThreshold, verdict.GetProperty("score").GetDouble());
    }

    [Fact]
    public async Task A_failed_run_is_blocking_and_the_judge_is_never_bothered()
    {
        var bench = new FakeTestBench().Fails();
        var judge = new FakeJudge().Approves();   // must not be consulted
        var channel = new ScriptedUserChannel().Decides("abort");
        var session = ForgeSession.Create(_workspace, "veille");

        var result = await Engine(session, FullRunners(HappyAssistant(), channel, bench, judge))
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Abandoned, result.Outcome);
        Assert.Equal(0, judge.Calls);

        var verdict = Events().Single(e => e.GetProperty("kind").GetString() == "verdict.ready");
        Assert.False(verdict.GetProperty("passing").GetBoolean());
        var finding = verdict.GetProperty("findings").EnumerateArray().First();
        Assert.Equal("blocking", finding.GetProperty("severity").GetString());
    }

    [Fact]
    public async Task A_promised_deliverable_that_never_appeared_is_a_finding()
    {
        // The valid blueprint promises /output/resume.md; the fake bench writes nothing.
        var bench = new FakeTestBench().Succeeds();
        var judge = new FakeJudge();   // degraded: the mechanical finding must decide alone
        var channel = new ScriptedUserChannel().Decides("abort");
        var session = ForgeSession.Create(_workspace, "veille");

        await Engine(session, FullRunners(HappyAssistant(), channel, bench, judge))
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        var verdict = Events().Single(e => e.GetProperty("kind").GetString() == "verdict.ready");
        Assert.False(verdict.GetProperty("passing").GetBoolean());
        Assert.Contains(
            verdict.GetProperty("findings").EnumerateArray(),
            f => f.GetProperty("statement").GetString()!.Contains("/output/resume.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dry_still_pauses_before_the_test()
    {
        var bench = new FakeTestBench();
        var session = ForgeSession.Create(_workspace, "veille");

        var result = await Engine(session, FullRunners(HappyAssistant(), new ScriptedUserChannel(), bench, new FakeJudge()))
            .RunAsync(stopBefore: ForgeState.Test, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Paused, result.Outcome);
        Assert.Equal(0, bench.Executions);
    }
}

/// <summary>The verdict schema: fence tolerance, clamping, and the recomputed passing rule.</summary>
public class ForgeVerdictTests
{
    [Fact]
    public void A_fenced_response_parses_and_passing_is_recomputed_not_trusted()
    {
        var text = """
            ```json
            { "score": 0.9, "passing": false, "findings": [], "suggestions": [] }
            ```
            """;

        Assert.True(ForgeVerdict.TryParse(text, out var verdict, out _));
        Assert.True(verdict!.Passing);            // 0.9 ≥ threshold, no blocking finding
        Assert.Equal(ForgeVerdict.JudgeLlm, verdict.Judge);
    }

    [Fact]
    public void A_blocking_finding_blocks_whatever_the_score_says()
    {
        var text = """{ "score": 0.95, "findings": [ { "id": "F1", "severity": "blocking", "statement": "s" } ] }""";

        Assert.True(ForgeVerdict.TryParse(text, out var verdict, out _));
        Assert.False(verdict!.Passing);
    }

    [Fact]
    public void The_score_is_clamped_into_the_unit_interval()
    {
        Assert.True(ForgeVerdict.TryParse("""{ "score": 7.5 }""", out var verdict, out _));
        Assert.Equal(1.0, verdict!.Score);
    }

    [Fact]
    public void Garbage_is_an_error_message_not_an_exception()
    {
        Assert.False(ForgeVerdict.TryParse("the crew did great!", out _, out var errors));
        Assert.NotEmpty(errors);
    }
}

/// <summary>The production judge over a scripted provider: parse, retry, and honest nulls.</summary>
public class LlmForgeJudgeTests
{
    private static ForgeBrief Brief()
    {
        Assert.True(ForgeBrief.TryParse(ForgeDocuments.ValidBrief, out var brief, out _));
        return brief!;
    }

    [Fact]
    public async Task A_clean_json_response_becomes_the_verdict()
    {
        var provider = new ScriptedLlmProvider()
            .Answers("""{ "score": 0.8, "findings": [], "suggestions": [] }""");

        var judgement = await new LlmForgeJudge(provider)
            .JudgeAsync(Brief(), "le résumé", TestContext.Current.CancellationToken);

        var verdict = judgement.Verdict;
        Assert.NotNull(verdict);
        Assert.True(verdict!.Passing);
        Assert.Equal(ForgeVerdict.JudgeLlm, verdict.Judge);
        Assert.Equal(120, judgement.Tokens);   // the ScriptedLlmProvider's per-answer usage

        // The criteria travelled in the judge's body, by id and in the user's words.
        var messages = Assert.Single(provider.Chats);
        Assert.Contains("A1", messages[^1].Content, StringComparison.Ordinal);
        Assert.Contains("cite ses sources", messages[^1].Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task One_garbage_response_earns_a_retry_with_the_parse_error()
    {
        var provider = new ScriptedLlmProvider()
            .Answers("great output, 10/10")
            .Answers("""{ "score": 0.75 }""");

        var judgement = await new LlmForgeJudge(provider)
            .JudgeAsync(Brief(), "le résumé", TestContext.Current.CancellationToken);

        Assert.NotNull(judgement.Verdict);
        Assert.Equal(240, judgement.Tokens);   // both attempts were paid for
        Assert.Equal(2, provider.Chats.Count);
        Assert.Contains("rejected", provider.Chats[1][^1].Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Two_garbage_responses_make_the_judge_unavailable_never_a_fake_score()
    {
        var provider = new ScriptedLlmProvider().Answers("meh").Answers("still meh");

        var judgement = await new LlmForgeJudge(provider)
            .JudgeAsync(Brief(), "le résumé", TestContext.Current.CancellationToken);

        Assert.Null(judgement.Verdict);
        Assert.Equal(240, judgement.Tokens);   // unavailable is not free — it was attempted
    }

    [Fact]
    public async Task No_provider_means_no_judge()
    {
        var judgement = await new LlmForgeJudge(null)
            .JudgeAsync(Brief(), "le résumé", TestContext.Current.CancellationToken);

        Assert.Null(judgement.Verdict);
        Assert.Equal(0, judgement.Tokens);
    }
}
