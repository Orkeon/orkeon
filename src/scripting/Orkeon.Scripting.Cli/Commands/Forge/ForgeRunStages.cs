using System.Globalization;
using Orkeon.Application.Crew;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// Live progress of the sandboxed run (SPEC-ORKEON-FORGE §9.2): the engine host's
/// <see cref="ICrewExecutionHook"/>, projecting each finished task onto the event stream.
/// The forge host registers no other hook — <c>AutoSummaryWriter</c> belongs to
/// <c>RunnerExecution</c>, not to <c>RunnerHost.Build</c> — so no composition is needed
/// here (verified; the spec's caution §9.2 targeted the runner hosts).
/// </summary>
internal sealed class ForgeRunObserver : ICrewExecutionHook
{
    private ForgeEventWriter? _events;

    /// <summary>Attaches the stream; before this, the observer stays silent.</summary>
    public void Attach(ForgeEventWriter events) => _events = events;

    /// <inheritdoc />
    public Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct)
    {
        _events?.Emit("task.completed", new
        {
            taskId = snapshot.TaskId,
            agentRole = snapshot.AgentRole,
            success = snapshot.Success,
            durationMs = (long)snapshot.Duration.TotalMilliseconds,
            tokens = snapshot.TokensUsed,
            toolCalls = snapshot.ToolCallCount,
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

    /// <summary>Builds the stage over the bench seam.</summary>
    public TestStage(IForgeTestBench bench) =>
        _bench = bench ?? throw new ArgumentNullException(nameof(bench));

    /// <inheritdoc />
    public ForgeState Stage => ForgeState.Test;

    /// <inheritdoc />
    public async Task<ForgeStageOutcome> RunAsync(
        ForgeSession session, ForgeEventWriter events, CancellationToken cancellationToken)
    {
        var runNumber = session.Document.Iteration;
        var runDirectory = Path.Combine(session.Directory, RunsDirectoryName, runNumber.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(runDirectory);

        events.Emit("run.started", new { run = runNumber, target = ForgeYamlRenderer.CrewDirectoryName });

        ForgeTestRun run;
        try
        {
            run = await _bench.ExecuteAsync(session, runNumber, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;   // interruption stays an interruption — the session is saved upstream
        }
#pragma warning disable CA1031 // stage fault barrier: a bench crash must end the cycle as a failure, not as an unhandled exception
        catch (Exception ex)
        {
            return new ForgeStageOutcome
            {
                Trigger = ForgeTrigger.Fail,
                Detail = $"The test bench crashed: {ex.Message}",
            };
        }
#pragma warning restore CA1031

        var relativeOutput = Path.Combine(RunsDirectoryName, runNumber.ToString(CultureInfo.InvariantCulture), "output.md");
        await File.WriteAllTextAsync(Path.Combine(session.Directory, relativeOutput), run.Output, cancellationToken)
            .ConfigureAwait(false);
        session.SaveArtifact(Path.Combine(RunsDirectoryName, runNumber.ToString(CultureInfo.InvariantCulture), "run.json"), run);
        session.SaveArtifact(LastRunFileName, run);

        // The run's /output mount is snapshotted into the run directory and cleared, so
        // deliverables never leak from one cycle into the next one's diagnosis.
        SnapshotOutputs(session.Directory, runDirectory);

        // W-08: the closing event carries what the trial itself cost — distinct from the
        // session-cumulative cost.updated, which folds in assistant and judge usage.
        events.Emit("run.finished", new
        {
            run = runNumber,
            success = run.Success,
            outputPath = relativeOutput,
            durationMs = run.DurationMs,
            tokens = run.Tokens,
            cacheHitTokens = run.CacheHitTokens,
            cacheMissTokens = run.CacheMissTokens,
        });

        return new ForgeStageOutcome
        {
            Trigger = ForgeTrigger.TestCompleted,
            TokensConsumed = run.Tokens ?? 0,
        };
    }

    private static void SnapshotOutputs(string sessionDirectory, string runDirectory)
    {
        var outputDirectory = Path.Combine(sessionDirectory, OutputDirectoryName);
        if (!Directory.Exists(outputDirectory))
            return;

        var snapshot = Path.Combine(runDirectory, OutputDirectoryName);
        Directory.CreateDirectory(snapshot);

        foreach (var file in Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(outputDirectory, file);
            var target = Path.Combine(snapshot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(file, target, overwrite: true);
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

    /// <summary>Builds the stage over the judge seam.</summary>
    public DiagnoseStage(IForgeJudge judge) =>
        _judge = judge ?? throw new ArgumentNullException(nameof(judge));

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

        var mechanical = MechanicalFindings(session, run);

        // A failed run never consults the judge — nothing to judge, nothing to pay.
        var judgement = run.Success && !string.IsNullOrWhiteSpace(run.Output)
            ? await _judge.JudgeAsync(brief, run.Output, cancellationToken).ConfigureAwait(false)
            : ForgeJudgement.Unavailable;

        ForgeVerdict verdict;
        if (judgement.Verdict is { } judged)
        {
            verdict = judged with { Findings = [.. mechanical, .. judged.Findings] };
            verdict = verdict with
            {
                Passing = verdict.Score >= ForgeVerdict.PassingThreshold && !verdict.HasBlockingFinding,
            };
        }
        else
        {
            // No judge (or nothing worth judging): mechanical checks only, and said so.
            var passing = mechanical.Count == 0;
            verdict = new ForgeVerdict
            {
                Score = passing ? ForgeVerdict.PassingThreshold : 0.0,
                Passing = passing,
                Findings = mechanical,
                Judge = ForgeVerdict.JudgeDeterministic,
            };
        }

        var runDirectory = Path.Combine(TestStage.RunsDirectoryName, run.Run.ToString(CultureInfo.InvariantCulture));
        session.SaveArtifact(Path.Combine(runDirectory, "verdict.json"), verdict);
        session.SaveArtifact("verdict.json", verdict);

        EmitVerdictReady(events, verdict, run);

        return new ForgeStageOutcome { Trigger = ForgeTrigger.Diagnosed, TokensConsumed = judgement.Tokens };
    }

    /// <summary>
    /// The one <c>verdict.ready</c> payload builder — the diagnosis and the Verdict
    /// stage's recall (a resumed arbitration) must never drift apart. The metrics are the
    /// LAST TRIAL's own (W-08), null when the run left them unmeasured.
    /// </summary>
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
            cacheHitTokens = run?.CacheHitTokens,
            cacheMissTokens = run?.CacheMissTokens,
        });
    }

    /// <summary>What no judge is needed to see; blocking when the run itself went wrong.</summary>
    private static List<ForgeFinding> MechanicalFindings(ForgeSession session, ForgeTestRun run)
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

        // Every deliverable the blueprint promised must exist in the run's snapshot.
        var blueprint = session.TryLoadArtifact<ForgeBlueprint>(ForgeSession.BlueprintFileName);
        foreach (var task in blueprint?.Tasks ?? [])
        {
            if (task.Deliverable is not { Length: > 0 } deliverable)
                continue;

            var relative = deliverable.TrimStart('/');
            if (relative.StartsWith("output/", StringComparison.Ordinal))
                relative = relative["output/".Length..];

            var expected = Path.Combine(
                session.Directory,
                TestStage.RunsDirectoryName,
                run.Run.ToString(CultureInfo.InvariantCulture),
                TestStage.OutputDirectoryName,
                relative);

            if (!File.Exists(expected))
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
        var verdict = session.TryLoadArtifact<ForgeVerdict>("verdict.json");
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

        if (!ForgeBlueprint.TryParse(json, out var blueprint, out var errors))
        {
            events.Error(ForgeErrorCodes.BlueprintInvalid, string.Join(" ", errors), recoverable: true);
            return false;
        }

        var compilation = ForgeBlueprintCompiler.Compile(blueprint!);
        var validation = ForgeBlueprintCompiler.Validate(compilation, _knownTools);
        if (validation.Errors.Count > 0)
        {
            events.Error(ForgeErrorCodes.BlueprintInvalid, string.Join(" ", validation.Errors), recoverable: true);
            return false;
        }

        session.SaveArtifact(ForgeSession.BlueprintFileName, blueprint!);
        // The engine registers the loop-back's iteration right after this stage returns:
        // the announcement carries the number the re-render will actually run as.
        events.Emit("blueprint.ready", new { blueprint, iteration = session.Document.Iteration + 1 });
        return true;
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
