using Orkeon.Hosting.Runs;
using System.Globalization;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Protocol;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Scripting.Cli.Commands.Run;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// Live progress of the sandboxed run (SPEC-ORKEON-FORGE §9.2): the engine host's
/// <see cref="ICrewExecutionHook"/>, projecting each finished task onto the event stream.
/// The forge host registers no other hook — <c>AutoSummaryWriter</c> belongs to
/// <c>RunnerExecution</c>, not to <c>RunnerHost.Build</c> — so no composition is needed
/// here (verified; the spec's caution §9.2 targeted the runner hosts).
/// </summary>
internal sealed class ForgeRunObserver : ICrewExecutionHook, ILlmCallObserver
{
    private ForgeEventWriter? _events;
    private RunEventsLlmCallObserver? _calls;

    private readonly object _gate = new();
    private readonly Dictionary<string, bool> _lastAttemptCapped = [];
    private readonly Dictionary<string, bool> _lastAttemptMissingDeliverable = [];

    /// <summary>Tasks whose last attempt ended on the iteration cap (a task seen twice counts once).</summary>
    public int CappedTasks
    {
        get { lock (_gate) { return _lastAttemptCapped.Values.Count(capped => capped); } }
    }

    /// <summary>Tasks whose last attempt succeeded without writing its <c>tool_call</c> deliverable (a task seen twice counts once, #392).</summary>
    public int MissingDeliverables
    {
        get { lock (_gate) { return _lastAttemptMissingDeliverable.Values.Count(missing => missing); } }
    }

    /// <summary>
    /// Starts a trial: forgets the previous trial's tasks so <see cref="CappedTasks"/> and
    /// <see cref="MissingDeliverables"/> count this trial only.
    /// </summary>
    public void BeginTrial()
    {
        lock (_gate)
        {
            _lastAttemptCapped.Clear();
            _lastAttemptMissingDeliverable.Clear();
        }
    }

    /// <summary>Attaches the stream; before this, the observer stays silent. <paramref name="withCalls"/> also writes the model calls (llm.call.*).</summary>
    public void Attach(ForgeEventWriter events, bool withCalls = false)
    {
        _events = events;
        _calls = withCalls ? new RunEventsLlmCallObserver(new ForgeCircuitSink(events, () => (_circuit, _cause)), TimeProvider.System) : null;
    }

    private string? _circuit;
    private string? _cause;

    /// <summary>
    /// The decision circuit of the pass that runs (#431): <c>task.completed</c> and the <c>llm.call.*</c> lines carry
    /// it, with its cause when off. Null leaves them as they were.
    /// </summary>
    public void SetCircuit(string? circuit, string? cause)
    {
        _circuit = circuit;
        _cause = cause;
    }

    /// <inheritdoc />
    public void Started(string callId, LlmCallMode mode, int estimatedPromptTokens) =>
        _calls?.Started(callId, mode, estimatedPromptTokens);

    /// <inheritdoc />
    public void Progress(string callId, int estimatedCompletionTokens) =>
        _calls?.Progress(callId, estimatedCompletionTokens);

    /// <inheritdoc />
    public void Finished(string callId, LlmCallMode mode, bool fellBack, int? promptTokens, int? completionTokens) =>
        _calls?.Finished(callId, mode, fellBack, promptTokens, completionTokens);

    /// <inheritdoc />
    public Task OnTaskStartedAsync(TaskStartSnapshot snapshot, CancellationToken ct) =>
        Task.CompletedTask;   // the forge stream keeps its own vocabulary; the trial reports finished tasks only

    /// <inheritdoc />
    public Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct)
    {
        lock (_gate)
        {
            _lastAttemptCapped[snapshot.TaskId] = snapshot.EndedOnIterationCap;
            // Only a task that succeeded counts: a failed one is never flagged (#392).
            _lastAttemptMissingDeliverable[snapshot.TaskId] = snapshot.Success && snapshot.DeliverableMissing;
        }

        _events?.Emit("task.completed", new
        {
            taskId = snapshot.TaskId,
            agentRole = snapshot.AgentRole,
            success = snapshot.Success,
            durationMs = (long)snapshot.Duration.TotalMilliseconds,
            tokens = snapshot.TokensUsed,
            toolCalls = snapshot.ToolCallCount,
            endedOnIterationCap = snapshot.EndedOnIterationCap,
            deliverableMissing = snapshot.DeliverableMissing,
            circuit = _circuit,
            cause = _cause,
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct) =>
        Task.CompletedTask;   // run.finished belongs to the stage, which owns the verdicting

    /// <inheritdoc />
    public Task OnCrewFailedAsync(CrewExecutionSnapshot isPartial, Exception? ex, CancellationToken ct) =>
        Task.CompletedTask;   // failure is detected on the CrewOutput shape (SPEC §9.3)
}

/// <summary>
/// The sandboxed test (SPEC-ORKEON-FORGE §9): runs the rendered crew on the brief's sample,
/// snapshots everything under <c>runs/&lt;n&gt;/</c>, and always hands over to the diagnosis
/// — a failed run is explained, never just reported.
/// </summary>
internal sealed class TestStage : IForgeStageRunner
{
    /// <summary>The per-session directory holding one sub-directory per run.</summary>
    public const string RunsDirectoryName = "runs";

    /// <summary>Where the run's <c>/output</c> mount lands physically, inside the session.</summary>
    public const string OutputDirectoryName = "output";

    /// <summary>The diagnosis's handle on the latest run.</summary>
    public const string LastRunFileName = "last-run.json";

    private readonly IForgeTestBench _bench;
    private readonly ForgeRunObserver _observer;
    private readonly DecisionCircuit? _hostCircuit;
    private readonly ForgeSystemOne _systemOne;

    /// <summary>
    /// Builds the stage over the bench seam; the observer supplies the capped-task count. <paramref name="hostCircuit"/>
    /// is the engine host's decision circuit (#431), which picks the passes of an <c>optional</c> crew's trial; null
    /// when unknown, read as on. <paramref name="systemOne"/> is the forge's own circuit (#435), which only estimates
    /// the trial for the user before it starts and never changes the trial's circuit; null asks nothing.
    /// </summary>
    public TestStage(IForgeTestBench bench, ForgeRunObserver observer, DecisionCircuit? hostCircuit = null, ForgeSystemOne? systemOne = null)
    {
        _bench = bench ?? throw new ArgumentNullException(nameof(bench));
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _hostCircuit = hostCircuit;
        _systemOne = systemOne ?? ForgeSystemOne.None;
    }

    /// <inheritdoc />
    public ForgeState Stage => ForgeState.Test;

    /// <summary>One pass to run: the circuit the bench is given (null: the host's) and the one the files name.</summary>
    private sealed record PassPlan(DecisionCircuit? Override, DecisionCircuit? Shown);

    /// <inheritdoc />
    public async Task<ForgeStageOutcome> RunAsync(
        ForgeSession session, ForgeEventWriter events, CancellationToken cancellationToken)
    {
        var runNumber = session.Document.Iteration;
        var runDirectory = Path.Combine(session.Directory, RunsDirectoryName, runNumber.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(runDirectory);

        var plans = PlanPasses(session, events, runDirectory);
        var split = plans.Count > 1;
        var passes = new List<(ForgeTestRun Run, ForgeTestPass Pass)>();
        var kept = split
            ? [.. plans.Select(plan => KeptPass(session, PassDirectory(runDirectory, plan)))]
            : new List<(ForgeTestRun Run, ForgeTestPass Pass)?> { null };

        // System One's estimate of the trial (#435), before run.started: for the user, it never skips a pass. A resume
        // that keeps a pass already ran (#431) was estimated before that pass, and is not asked or charged again.
        var usage = kept.Any(static pass => pass is not null)
            ? default
            : await _systemOne.EstimateTrialAsync(
                events,
                session.TryLoadArtifact<ForgeBrief>(ForgeSession.BriefFileName),
                session.TryLoadArtifact<ForgeBlueprint>(ForgeSession.BlueprintFileName),
                cancellationToken).ConfigureAwait(false);

        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            var passDirectory = split ? PassDirectory(runDirectory, plan) : runDirectory;

            // A resume after a budget stop between passes (#431): the pass that ran is kept, never run again.
            if (kept[index] is { } keptPass)
            {
                passes.Add(keptPass);
                continue;
            }

            var ran = await RunPassAsync(session, events, runNumber, index + 1, plans.Count, plan, passDirectory, cancellationToken)
                .ConfigureAwait(false);
            if (ran.Failure is { } failure)
                return failure with { Usage = usage };

            passes.Add((ran.Run!, ran.Pass!));
            usage = usage.Plus(TrialUsage(ran.Run!));

            // The on pass is charged before the off pass starts: a budget it used up ends the session here,
            // resumable at the trial with runs/<n>/on/ kept.
            if (index + 1 < plans.Count
                && session.Document.Budget.ExhaustedDimensionAfter(usage, ran.Run!.DurationMs / 1000) is { } dimension)
            {
                return new ForgeStageOutcome { Trigger = ForgeTrigger.TestCompleted, Usage = usage, BudgetExhausted = dimension };
            }
        }

        _observer.SetCircuit(null, null);
        var run = Combine(runNumber, passes, plans);
        session.SaveArtifact(Path.Combine(RunsDirectoryName, runNumber.ToString(CultureInfo.InvariantCulture), "run.json"), run);
        session.SaveArtifact(LastRunFileName, run);

        return new ForgeStageOutcome
        {
            Trigger = ForgeTrigger.TestCompleted,
            Usage = usage,
        };
    }

    /// <summary>The folder of one pass of a two-pass trial: <c>runs/&lt;n&gt;/on</c> or <c>runs/&lt;n&gt;/off</c>.</summary>
    private static string PassDirectory(string runDirectory, PassPlan plan) =>
        Path.Combine(runDirectory, ForgeCrewDecision.CircuitName(plan.Shown ?? DecisionCircuit.On));

    /// <summary>The pass a budget stop left in <paramref name="passDirectory"/> (#431); null when none ran there.</summary>
    private static (ForgeTestRun Run, ForgeTestPass Pass)? KeptPass(ForgeSession session, string passDirectory) =>
        session.TryLoadArtifact<ForgeTestRun>(Path.GetRelativePath(session.Directory, Path.Combine(passDirectory, "run.json"))) is { } kept
        && kept.Passes is [{ } keptPass]
            ? (kept, keptPass)
            : null;

    /// <summary>
    /// The passes of this trial (#431): on then off for an <c>optional</c> crew, off alone, warned, when the host's
    /// circuit is off; one pass on the host's circuit otherwise. A resume whose on pass ran before a budget stop keeps
    /// both passes even when the host's circuit is off since: the on pass is kept, and the off pass runs.
    /// </summary>
    private List<PassPlan> PlanPasses(ForgeSession session, ForgeEventWriter events, string runDirectory)
    {
        if (ForgeCrewDecision.Normalize(session.Document.CrewDecision) != ForgeCrewDecision.Optional)
            return [new PassPlan(null, _hostCircuit)];

        var host = _hostCircuit ?? DecisionCircuit.On;
        var circuits = ForgeCrewDecision.PassesOf(ForgeCrewDecision.Optional, host);
        var on = new PassPlan(DecisionCircuit.On, DecisionCircuit.On);
        if (!host.Enabled && KeptPass(session, PassDirectory(runDirectory, on)) is not null)
            return [on, new PassPlan(host, host)];

        if (!host.Enabled)
        {
            events.Emit("warning", new
            {
                code = ForgeCrewDecision.PassSkippedWarning,
                message = $"The crew is 'optional' and {host.OffReason}: its trial runs without System One only.",
                cause = ForgeCrewDecision.CauseName(host),
            });
        }

        return [.. circuits.Select(static circuit => new PassPlan(circuit, circuit))];
    }

    /// <summary>One pass: the crew run once, its output, its run file and its folders under <paramref name="passDirectory"/>.</summary>
    private async Task<(ForgeTestRun? Run, ForgeTestPass? Pass, ForgeStageOutcome? Failure)> RunPassAsync(
        ForgeSession session, ForgeEventWriter events, int runNumber, int pass, int passCount, PassPlan plan,
        string passDirectory, CancellationToken cancellationToken)
    {
        _observer.BeginTrial();
        Directory.CreateDirectory(passDirectory);
        var circuit = plan.Shown is { } shown ? ForgeCrewDecision.CircuitName(shown) : null;
        var cause = plan.Shown is { } off ? ForgeCrewDecision.CauseName(off) : null;
        _observer.SetCircuit(circuit, cause);

        events.Emit("run.started", new
        {
            run = runNumber,
            target = ForgeYamlRenderer.CrewDirectoryName,
            passes = passCount,
            pass,
            circuit,
            cause,
        });

        ForgeTestRun run;
        try
        {
            run = await _bench.ExecuteAsync(session, runNumber, plan.Override, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;   // interruption stays an interruption — the session is saved upstream
        }
#pragma warning disable CA1031 // stage fault barrier: a bench crash must end the cycle as a failure, not as an unhandled exception
        catch (Exception ex)
        {
            // The trial ends on a failure, but its closing event is still written: a consumer
            // of the stream waits for run.finished, whatever the verdict.
            EmitRunFinished(events, runNumber, success: false, outputPath: null, durationMs: 0, tokens: 0,
                promptTokens: 0, completionTokens: 0, cacheHitTokens: 0, cacheMissTokens: 0, mailActions: Array.Empty<object>(),
                circuit, cause);
            _observer.SetCircuit(null, null);
            return (null, null, new ForgeStageOutcome
            {
                Trigger = ForgeTrigger.Fail,
                Detail = $"The test bench crashed: {ex.Message}",
            });
        }
#pragma warning restore CA1031

        var relativeOutput = Path.GetRelativePath(session.Directory, Path.Combine(passDirectory, "output.md"));
        await File.WriteAllTextAsync(Path.Combine(session.Directory, relativeOutput), run.Output, cancellationToken)
            .ConfigureAwait(false);

        var passRecord = new ForgeTestPass
        {
            Circuit = circuit,
            Cause = cause,
            Success = run.Success,
            OutputPath = relativeOutput.Replace(Path.DirectorySeparatorChar, '/'),
            Error = run.Error,
            DurationMs = run.DurationMs,
            Tokens = run.Tokens,
            PromptTokens = run.PromptTokens,
            CompletionTokens = run.CompletionTokens,
            CacheHitTokens = run.CacheHitTokens,
            CacheMissTokens = run.CacheMissTokens,
            MailActions = run.MailActions,
        };
        run = run with { Circuit = circuit, Passes = [passRecord] };
        if (passCount > 1)
            session.SaveArtifact(Path.GetRelativePath(session.Directory, Path.Combine(passDirectory, "run.json")), run);

        // The run's output folders are snapshotted into the pass directory and cleared, so
        // deliverables never leak from one pass, or one cycle, into the next one's diagnosis.
        SnapshotOutputs(session, passDirectory);

        // W-08: the closing event carries what the trial itself cost — distinct from the
        // session-cumulative cost.updated, which folds in assistant and judge usage.
        EmitRunFinished(events, runNumber, run.Success, relativeOutput, run.DurationMs, run.Tokens, run.PromptTokens,
            run.CompletionTokens, run.CacheHitTokens, run.CacheMissTokens, run.MailActions, circuit, cause);
        return (run, passRecord, null);
    }

    /// <summary>
    /// The trial's record over its passes (#431): today's fields, the six figures summed, success when every pass
    /// succeeded, the last pass's output, the mail actions in pass order, and <c>passes[]</c>.
    /// </summary>
    private static ForgeTestRun Combine(int runNumber, List<(ForgeTestRun Run, ForgeTestPass Pass)> passes, List<PassPlan> plans)
    {
        if (passes.Count == 1)
            return passes[0].Run with { Run = runNumber };

        var last = passes[^1].Run;
        return last with
        {
            Run = runNumber,
            Success = passes.All(static p => p.Run.Success),
            Output = last.Output,
            DurationMs = passes.Sum(static p => p.Run.DurationMs),
            Tokens = Sum(passes, static r => r.Tokens),
            PromptTokens = Sum(passes, static r => r.PromptTokens),
            CompletionTokens = Sum(passes, static r => r.CompletionTokens),
            CacheHitTokens = Sum(passes, static r => r.CacheHitTokens),
            CacheMissTokens = Sum(passes, static r => r.CacheMissTokens),
            Error = passes.Select(static p => p.Run.Error).FirstOrDefault(static e => e is not null),
            MailActions = [.. passes.SelectMany(static p => p.Run.MailActions)],
            Circuit = plans.Count == 1 ? last.Circuit : null,
            Passes = [.. passes.Select(static p => p.Pass)],
        };
    }

    private static long? Sum(List<(ForgeTestRun Run, ForgeTestPass Pass)> passes, Func<ForgeTestRun, long?> figure) =>
        passes.All(p => figure(p.Run) is null) ? null : passes.Sum(p => figure(p.Run) ?? 0);

    private void EmitRunFinished(
        ForgeEventWriter events, int runNumber, bool success, string? outputPath, long? durationMs, long? tokens,
        long? promptTokens, long? completionTokens, long? cacheHitTokens, long? cacheMissTokens, object mailActions,
        string? circuit, string? cause) =>
        events.Emit("run.finished", new
        {
            run = runNumber,
            success,
            outputPath,
            durationMs,
            tokens,
            promptTokens,
            completionTokens,
            cacheHitTokens,
            cacheMissTokens,
            mailActions,
            cappedTasks = _observer.CappedTasks,
            missingDeliverables = _observer.MissingDeliverables,
            circuit,
            cause,
        });

    /// <summary>
    /// What the trial itself cost, as the budget will charge it. A run that reported only
    /// a grand total puts everything on the ascending side rather than inventing a split:
    /// the budget meters the sum either way, and a half-known figure is not improved by
    /// halving it arbitrarily.
    /// </summary>
    private static ForgeUsageSnapshot TrialUsage(ForgeTestRun run) => run switch
    {
        { PromptTokens: { } up, CompletionTokens: { } down } => new ForgeUsageSnapshot(up, down, 0),
        { Tokens: { } total } => new ForgeUsageSnapshot(total, 0, 0),
        _ => default,
    };

    /// <summary>
    /// Moves what the run wrote into <c>runs/&lt;n&gt;/folders/&lt;name&gt;</c>, one directory per
    /// confirmed output folder — the inputs the session holds stay where they are.
    /// </summary>
    private static void SnapshotOutputs(ForgeSession session, string runDirectory)
    {
        foreach (var folder in ForgeFolders.Of(session).Where(f => !f.IsInput))
        {
            var written = ForgeFolders.SessionFolder(session, folder);
            if (!Directory.Exists(written))
                continue;

            var snapshot = Path.Combine(runDirectory, ForgeFolders.SessionDirectoryName, folder.Name);
            Directory.CreateDirectory(snapshot);

            foreach (var file in Directory.EnumerateFiles(written, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(written, file);
                var target = Path.Combine(snapshot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file, target, overwrite: true);
            }
        }
    }
}

/// <summary>
/// The diagnosis (SPEC-ORKEON-FORGE §10): mechanical checks first — run completion, output
/// presence, promised deliverables — then the judge against the acceptance criteria, when
/// one is available. A degraded verdict says it is one; it never fakes a score.
/// </summary>
internal sealed class DiagnoseStage : IForgeStageRunner
{
    private readonly IForgeJudge _judge;
    private readonly ForgeSystemOne _systemOne;

    /// <summary>
    /// Builds the stage over the judge seam. <paramref name="systemOne"/> is the forge's own circuit (#435): on, it
    /// reads the output against each acceptance criterion after the judge, for the user only; null asks nothing.
    /// </summary>
    public DiagnoseStage(IForgeJudge judge, ForgeSystemOne? systemOne = null)
    {
        _judge = judge ?? throw new ArgumentNullException(nameof(judge));
        _systemOne = systemOne ?? ForgeSystemOne.None;
    }

    /// <inheritdoc />
    public ForgeState Stage => ForgeState.Diagnose;

    /// <inheritdoc />
    public async Task<ForgeStageOutcome> RunAsync(
        ForgeSession session, ForgeEventWriter events, CancellationToken cancellationToken)
    {
        var brief = session.TryLoadArtifact<ForgeBrief>(ForgeSession.BriefFileName);
        var run = session.TryLoadArtifact<ForgeTestRun>(TestStage.LastRunFileName);
        if (brief is null || run is null)
        {
            return new ForgeStageOutcome
            {
                Trigger = ForgeTrigger.Fail,
                FailureCode = ForgeErrorCodes.SessionCorrupt,
                Detail = "The diagnosis needs the brief and the last run, and one of them is missing.",
            };
        }

        // Each pass is judged on its own, with the same prompt; the verdict combines them (#431).
        var runDirectory = Path.Combine(TestStage.RunsDirectoryName, run.Run.ToString(CultureInfo.InvariantCulture));
        IReadOnlyList<ForgeTestPass?> passes = run.Passes is { Count: > 1 } split ? [.. split] : [null];
        var judged = new List<(ForgeVerdict Verdict, string? Circuit)>();
        var usage = default(ForgeUsageSnapshot);
        foreach (var pass in passes)
        {
            var view = pass is null ? run : PassView(session, run, pass);
            var snapshot = pass is null ? runDirectory : Path.Combine(runDirectory, pass.Circuit ?? "");
            var (passVerdict, passUsage) = await JudgePassAsync(session, brief, view, snapshot, cancellationToken).ConfigureAwait(false);
            usage = usage.Plus(passUsage);
            judged.Add((passVerdict, pass?.Circuit ?? run.Circuit));
        }

        var verdict = Combined(session, run, judged);
        session.SaveArtifact(Path.Combine(runDirectory, ForgeSession.VerdictFileName), verdict);
        session.SaveArtifact(ForgeSession.VerdictFileName, verdict);

        EmitVerdictReady(events, verdict, run);

        // System One's reading of each criterion (#435), after the judge: for the user only, never in verdict.json,
        // so the arbitration and --auto read the judge's verdict alone.
        if (run.Success && !string.IsNullOrWhiteSpace(run.Output))
            usage = usage.Plus(await _systemOne.ReadCriteriaAsync(events, brief, run.Output, cancellationToken).ConfigureAwait(false));

        return new ForgeStageOutcome { Trigger = ForgeTrigger.Diagnosed, Usage = usage };
    }

    /// <summary>The pass of a two-pass trial as a run of its own: its output, figures and mail actions.</summary>
    private static ForgeTestRun PassView(ForgeSession session, ForgeTestRun run, ForgeTestPass pass)
    {
        var outputPath = pass.OutputPath is { } relative ? Path.Combine(session.Directory, relative) : null;
        return run with
        {
            Success = pass.Success,
            Output = outputPath is not null && File.Exists(outputPath) ? File.ReadAllText(outputPath) : "",
            Error = pass.Error,
            DurationMs = pass.DurationMs,
            Tokens = pass.Tokens,
            MailActions = pass.MailActions,
        };
    }

    /// <summary>One pass judged: the mechanical checks over its snapshot, then the judge, combined as before.</summary>
    private async Task<(ForgeVerdict Verdict, ForgeUsageSnapshot Usage)> JudgePassAsync(
        ForgeSession session, ForgeBrief brief, ForgeTestRun run, string snapshotDirectory, CancellationToken cancellationToken)
    {
        var mechanical = MechanicalFindings(session, run, snapshotDirectory);

        // A failed run never consults the judge — nothing to judge, nothing to pay.
        var judgement = run.Success && !string.IsNullOrWhiteSpace(run.Output)
            ? await _judge.JudgeAsync(brief, run.Output, run.MailActions, cancellationToken).ConfigureAwait(false)
            : ForgeJudgement.Unavailable;

        if (judgement.Verdict is { } judged)
        {
            var verdict = judged with { Findings = [.. mechanical, .. judged.Findings] };
            return (verdict with
            {
                Passing = verdict.Score >= ForgeVerdict.PassingThreshold && !verdict.HasBlockingFinding,
            }, judgement.Usage);
        }

        // No judge (or nothing worth judging): mechanical checks only, and said so.
        var passing = mechanical.Count == 0;
        return (new ForgeVerdict
        {
            Score = passing ? ForgeVerdict.PassingThreshold : 0.0,
            Passing = passing,
            Findings = mechanical,
            Judge = ForgeVerdict.JudgeDeterministic,
        }, judgement.Usage);
    }

    /// <summary>
    /// The verdict over the passes (#431): passing only when every pass passes, the lowest score, every finding
    /// tagged with its pass's circuit in an <c>optional</c> crew, and <c>F-PASS-ON-SKIPPED</c> (info) when its trial
    /// ran without its on pass.
    /// </summary>
    private static ForgeVerdict Combined(ForgeSession session, ForgeTestRun run, List<(ForgeVerdict Verdict, string? Circuit)> judged)
    {
        var optional = ForgeCrewDecision.Normalize(session.Document.CrewDecision) == ForgeCrewDecision.Optional;
        var findings = judged
            .SelectMany(j => j.Verdict.Findings.Select(f => optional ? f with { Circuit = j.Circuit } : f))
            .ToList();
        if (optional && judged.Count == 1 && run.Passes is [{ Circuit: DecisionCircuitStates.Off } offPass])
        {
            findings.Add(new ForgeFinding
            {
                Id = ForgeCrewDecision.PassOnSkippedFinding,
                Severity = "info",
                Statement = "The trial ran without System One only: the crew's run with a decision model was not tried.",
                Evidence = offPass.Cause,
                Circuit = DecisionCircuitStates.Off,
            });
        }

        return judged[0].Verdict with
        {
            Score = judged.Min(static j => j.Verdict.Score),
            Passing = judged.All(static j => j.Verdict.Passing),
            Findings = findings,
            Suggestions = [.. judged.SelectMany(static j => j.Verdict.Suggestions)],
            Passes = [.. judged.Select(static j => new ForgeVerdictPass { Circuit = j.Circuit, Passing = j.Verdict.Passing, Score = j.Verdict.Score })],
        };
    }

    internal static void EmitVerdictReady(ForgeEventWriter events, ForgeVerdict verdict, ForgeTestRun? run)
    {
        events.Emit("verdict.ready", new
        {
            score = verdict.Score,
            passing = verdict.Passing,
            findings = verdict.Findings,
            suggestions = verdict.Suggestions,
            judge = verdict.Judge,
            durationMs = run?.DurationMs,
            tokens = run?.Tokens,
            promptTokens = run?.PromptTokens,
            completionTokens = run?.CompletionTokens,
            cacheHitTokens = run?.CacheHitTokens,
            cacheMissTokens = run?.CacheMissTokens,
            // Each pass's verdict and its six figures, flat beside it (#431); the top level above holds their sums.
            passes = verdict.Passes?
                .Select(pass => (Verdict: pass, Ran: run?.Passes?.FirstOrDefault(p => p.Circuit == pass.Circuit)))
                .Select(static pass => new
                {
                    circuit = pass.Verdict.Circuit,
                    passing = pass.Verdict.Passing,
                    score = pass.Verdict.Score,
                    durationMs = pass.Ran?.DurationMs,
                    tokens = pass.Ran?.Tokens,
                    promptTokens = pass.Ran?.PromptTokens,
                    completionTokens = pass.Ran?.CompletionTokens,
                    cacheHitTokens = pass.Ran?.CacheHitTokens,
                    cacheMissTokens = pass.Ran?.CacheMissTokens,
                }),
        });
    }

    /// <summary>What no judge is needed to see; blocking when the run itself went wrong.</summary>
    private static List<ForgeFinding> MechanicalFindings(ForgeSession session, ForgeTestRun run, string snapshotDirectory)
    {
        var findings = new List<ForgeFinding>();

        if (!run.Success)
        {
            findings.Add(new ForgeFinding
            {
                Id = "F-RUN",
                Severity = "blocking",
                Statement = "The crew did not complete its run.",
                Evidence = run.Error ?? run.Output,
            });
            return findings;
        }

        if (string.IsNullOrWhiteSpace(run.Output))
        {
            findings.Add(new ForgeFinding
            {
                Id = "F-EMPTY",
                Severity = "blocking",
                Statement = "The run produced no output.",
            });
        }

        // Every deliverable the blueprint promised must exist in the run's snapshot, under the
        // output folder it lands in: /outmd/x.md is runs/<n>/folders/outmd/x.md.
        var blueprint = session.TryLoadArtifact<ForgeBlueprint>(ForgeSession.BlueprintFileName);
        var folders = ForgeFolders.Of(session);
        foreach (var task in blueprint?.Tasks ?? [])
        {
            if (task.Deliverable is not { Length: > 0 } deliverable)
                continue;

            var folder = ForgeFolders.OutputOf(deliverable, folders);
            var expected = folder is null || deliverable.Length <= folder.Path!.Length + 1
                ? null
                : Path.Combine(
                    session.Directory,
                    snapshotDirectory,
                    ForgeFolders.SessionDirectoryName,
                    folder.Name,
                    deliverable[(folder.Path!.Length + 1)..].Replace('/', Path.DirectorySeparatorChar));

            if (expected is null || !File.Exists(expected))
            {
                findings.Add(new ForgeFinding
                {
                    Id = $"F-DELIVERABLE-{task.Key}",
                    Severity = "major",
                    Statement = $"Task '{task.Key}' promised '{deliverable}' and the run did not produce it.",
                });
            }
        }

        return findings;
    }
}

/// <summary>
/// The arbitration (SPEC-ORKEON-FORGE §10): in interactive mode every verdict is the
/// user's call — <c>accept</c>, <c>refine</c>, <c>edit</c> (hand back an amended
/// blueprint), or <c>abort</c>; <c>--auto</c> accepts a conforming verdict and only ever
/// refines within the budget. A refine folds the findings and suggestions back into the
/// blueprint prompt, verbatim; an edit re-renders deterministically, zero LLM tokens.
/// </summary>
internal sealed class VerdictStage : IForgeStageRunner
{
    private static readonly string[] DecisionOptions = ["accept", "retry", "refine", "edit", "abort"];

    private readonly bool _auto;
    private readonly IForgeUserChannel _channel;
    private readonly IReadOnlyCollection<string> _knownTools;
    private bool _recallVerdict;

    /// <summary>
    /// Builds the stage; <paramref name="auto"/> arbitrates without a human;
    /// <paramref name="recallVerdict"/> re-emits the stored verdict before the first
    /// arbitration — a session resumed AT the arbitration (a reopen, or an interruption)
    /// would otherwise stream a <c>decision.needed</c> with no verdict on the wire.
    /// </summary>
    public VerdictStage(
        bool auto,
        IForgeUserChannel channel,
        IReadOnlyCollection<string> knownTools,
        bool recallVerdict = false)
    {
        _auto = auto;
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _knownTools = knownTools ?? throw new ArgumentNullException(nameof(knownTools));
        _recallVerdict = recallVerdict;
    }

    /// <inheritdoc />
    public ForgeState Stage => ForgeState.Verdict;

    /// <inheritdoc />
    public async Task<ForgeStageOutcome> RunAsync(
        ForgeSession session, ForgeEventWriter events, CancellationToken cancellationToken)
    {
        var verdict = session.TryLoadArtifact<ForgeVerdict>(ForgeSession.VerdictFileName);
        if (verdict is null)
        {
            return new ForgeStageOutcome
            {
                Trigger = ForgeTrigger.Fail,
                FailureCode = ForgeErrorCodes.SessionCorrupt,
                Detail = "The arbitration needs a verdict, and none is saved.",
            };
        }

        if (_recallVerdict)
        {
            // One recall per process: the loop-backs below re-earn their verdict through
            // Diagnose, which emits its own.
            _recallVerdict = false;
            DiagnoseStage.EmitVerdictReady(
                events, verdict, session.TryLoadArtifact<ForgeTestRun>(TestStage.LastRunFileName));
        }

        if (_auto)
        {
            if (verdict.Passing)
                return new ForgeStageOutcome { Trigger = ForgeTrigger.Accepted };

            FeedRefine(session, verdict);
            return new ForgeStageOutcome { Trigger = ForgeTrigger.RefineRequested };
        }

        // Interactive mode asks even on a passing verdict (remediation v2): the user may
        // still want to amend an agent before adopting, and "accept" costs one click.
        while (true)
        {
            events.Emit("decision.needed", new { options = DecisionOptions });
            var decision = await _channel.ReadDecisionAsync(DecisionOptions, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new OperationCanceledException("The user channel closed at the arbitration.");

            switch (decision)
            {
                case "accept":
                    // Keeping a non-conforming result is legitimate — the user judged on sight.
                    return new ForgeStageOutcome { Trigger = ForgeTrigger.Accepted };

                case "retry":
                    // Same blueprint, same render, a fresh run and a fresh verdict. The
                    // re-run is a cycle: refuse it BEFORE the trigger when no iteration
                    // remains — FinishBudgetExhausted mid-arbitration would close the
                    // stream and take accept/abort away from the user.
                    if (!session.Document.Budget.CanStartIteration)
                    {
                        events.Error(
                            ForgeEngine.CodeBudgetExhausted,
                            "No iteration remains in the budget for the re-run; raise --max-iterations and resume, or accept/abort.",
                            recoverable: true);
                        continue;
                    }

                    return new ForgeStageOutcome { Trigger = ForgeTrigger.RetryRequested };

                case "refine":
                    FeedRefine(session, verdict);
                    return new ForgeStageOutcome { Trigger = ForgeTrigger.RefineRequested };

                case "edit":
                    if (await ReadEditedBlueprintAsync(session, events, cancellationToken).ConfigureAwait(false))
                        return new ForgeStageOutcome { Trigger = ForgeTrigger.BlueprintEdited };
                    // Invalid edit: the error is on the stream, the arbitration re-opens.
                    continue;

                default:
                    return new ForgeStageOutcome { Trigger = ForgeTrigger.Abandon };
            }
        }
    }

    /// <summary>
    /// Reads and fully validates the amended blueprint — the same parse, compile and
    /// tool-catalogue checks a generated one goes through. A valid edit replaces the
    /// blueprint artifact and re-announces <c>blueprint.ready</c>; an invalid one is a
    /// recoverable <c>FORGE-BLUEPRINT-INVALID</c> and the decision is asked again.
    /// </summary>
    private async Task<bool> ReadEditedBlueprintAsync(
        ForgeSession session, ForgeEventWriter events, CancellationToken cancellationToken)
    {
        var json = await _channel.ReadBlueprintAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new OperationCanceledException("The user channel closed while sending the edited blueprint.");

        // The edit's re-render/re-test is a cycle: refuse it here, BEFORE the artifact is
        // replaced, when no iteration remains — otherwise the disk would hold an edited
        // blueprint over crew files rendered from the old one, and a raised-budget resume
        // could promote the mismatch.
        if (!session.Document.Budget.CanStartIteration)
        {
            events.Error(
                ForgeEngine.CodeBudgetExhausted,
                "No iteration remains in the budget for the edit's re-render; raise --max-iterations and resume, or accept/abort.",
                recoverable: true);
            return false;
        }

        if (ValidateEditedBlueprint(json, _knownTools, events, ForgeFolders.ExistingPaths(session), ForgeFolders.Of(session), session.Document.CrewDecision) is not { } blueprint)
            return false;

        session.SaveArtifact(ForgeSession.BlueprintFileName, blueprint);
        // The engine registers the loop-back's iteration right after this stage returns:
        // the announcement carries the number the re-render will actually run as.
        events.Emit("blueprint.ready", new { blueprint, iteration = session.Document.Iteration + 1 });
        return true;
    }

    /// <summary>
    /// Validates one amended blueprint in full — the same parse, compile and
    /// tool-catalogue checks a generated one goes through. Returns null when it is
    /// invalid, with the recoverable <c>FORGE-BLUEPRINT-INVALID</c> already on the
    /// stream. Shared between the arbitration's edit decision and
    /// <c>forge resume --edit</c> at the dry pause.
    /// </summary>
    internal static ForgeBlueprint? ValidateEditedBlueprint(
        string json, IReadOnlyCollection<string> knownTools, ForgeEventWriter events, IReadOnlyCollection<string> existingPaths,
        IReadOnlyList<ForgeFolder>? folders = null, string? crewDecision = null)
    {
        if (!ForgeBlueprint.TryParse(json, out var blueprint, out var errors, existingPaths, crewDecision))
        {
            events.Error(ForgeErrorCodes.BlueprintInvalid, string.Join(" ", errors), recoverable: true);
            return null;
        }

        var compilation = ForgeBlueprintCompiler.Compile(blueprint!, folders, crewDecision);
        var validation = ForgeBlueprintCompiler.Validate(compilation, knownTools, folders);
        if (validation.Errors.Count > 0)
        {
            events.Error(ForgeErrorCodes.BlueprintInvalid, string.Join(" ", validation.Errors), recoverable: true);
            return null;
        }

        return blueprint;
    }

    /// <summary>The diagnosis becomes the next blueprint turn's error feed, verbatim (SPEC §4).</summary>
    private static void FeedRefine(ForgeSession session, ForgeVerdict verdict)
    {
        var feed = new List<string>();
        foreach (var finding in verdict.Findings)
            feed.Add($"[{finding.Severity}] {finding.Statement}" + (finding.Acceptance is { } a ? $" (criterion {a})" : ""));
        foreach (var suggestion in verdict.Suggestions)
            feed.Add($"Suggested change on {suggestion.Target}: {suggestion.Change} — {suggestion.Reason}");

        session.SaveArtifact(ForgeSession.RepairFileName, new ForgeRepairState { Errors = feed });
    }
}
