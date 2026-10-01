using System.Text.Json.Serialization;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>The pending validation errors a repair must address, persisted with the session.</summary>
internal sealed record ForgeRepairState
{
    /// <summary>Validator output, verbatim — the repair prompt carries it untranslated.</summary>
    [JsonPropertyName("errors")]
    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>
/// The interview (SPEC-ORKEON-FORGE §7.2): the assistant converses until it submits a brief
/// through <c>brief_submit</c>; the engine validates the submission against the schema and
/// never trusts the format. A closed user channel is an interruption, not an answer.
/// </summary>
internal sealed class BriefStage : IForgeStageRunner
{
    /// <summary>Schema-invalid submissions tolerated before the stage gives up.</summary>
    public const int MaxSubmissionAttempts = 3;

    /// <summary>Assistant turns tolerated in one stage run — the backstop against a chat loop.</summary>
    public const int MaxTurns = 24;

    private readonly IForgeAssistant _assistant;
    private readonly IForgeUserChannel _channel;
    private readonly string? _initialNeed;
    private readonly bool _autoConfirmFolders;

    /// <summary>
    /// Builds the stage; <paramref name="initialNeed"/> is the need typed on the command line,
    /// when any; <paramref name="autoConfirmFolders"/> takes the proposed folders as they are
    /// (<c>--auto</c>) instead of asking.
    /// </summary>
    public BriefStage(
        IForgeAssistant assistant, IForgeUserChannel channel, string? initialNeed = null, bool autoConfirmFolders = false)
    {
        _assistant = assistant ?? throw new ArgumentNullException(nameof(assistant));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _initialNeed = initialNeed;
        _autoConfirmFolders = autoConfirmFolders;
    }

    /// <inheritdoc />
    public ForgeState Stage => ForgeState.Brief;

    /// <inheritdoc />
    public async Task<ForgeStageOutcome> RunAsync(
        ForgeSession session, ForgeEventWriter events, CancellationToken cancellationToken)
    {
        // A resumed session that already interviewed does not re-interview — but a brief whose
        // folders were never confirmed (the run stopped at the question) asks that one again.
        if (session.TryLoadArtifact<ForgeBrief>(ForgeSession.BriefFileName) is { } saved)
        {
            if (ForgeFolders.Confirmed(session) is null)
                await ConfirmFoldersAsync(session, events, saved, cancellationToken).ConfigureAwait(false);
            return new ForgeStageOutcome { Trigger = ForgeTrigger.BriefSubmitted };
        }

        var usage = default(ForgeUsageSnapshot);
        var submissionAttempts = 0;
        var userMessage = _initialNeed;
        IReadOnlyList<string>? errors = null;

        for (var turn = 0; turn < MaxTurns; turn++)
        {
            // Live, per model call, not at the stage boundary. The interview is ONE stage
            // of up to twenty-four turns, and one turn is several calls — the meter used to
            // stand at zero for the whole conversation and then jump. `charged` is what the
            // budget already holds; the engine charges this stage once, at the end.
            var charged = usage;
            var reply = await _assistant.NextAsync(
                new ForgeAssistantRequest { Phase = ForgeAssistantPhase.Brief, UserMessage = userMessage, Errors = errors },
                partial => ForgeCost.Emit(events, session.Document.Budget, charged.Plus(partial)),
                cancellationToken).ConfigureAwait(false);
            usage = usage.Plus(reply.Usage);

            if (reply.BriefJson is { } json)
            {
                var outcome = HandleSubmission(session, events, json, usage, ref submissionAttempts, ref errors);
                if (outcome is { Trigger: ForgeTrigger.BriefSubmitted })
                {
                    // The folders step (STUDIO-46): between the brief and the plan, the user
                    // confirms the folders the request named — or the defaults.
                    var brief = session.TryLoadArtifact<ForgeBrief>(ForgeSession.BriefFileName)!;
                    await ConfirmFoldersAsync(session, events, brief, cancellationToken).ConfigureAwait(false);
                    return outcome;
                }

                if (outcome is not null)
                    return outcome;

                // The errors go back into the next turn, verbatim; no user round-trip needed.
                userMessage = null;
                continue;
            }

            if (reply.Message is { Length: > 0 } message)
            {
                userMessage = await AskUserAsync(events, message, cancellationToken).ConfigureAwait(false);
                errors = null;
                continue;
            }

            // Neither a message nor a submission: a broken turn.
            userMessage = null;
            errors = ["The turn produced neither a message nor a submission."];
            submissionAttempts++;
            if (submissionAttempts >= MaxSubmissionAttempts)
                return Abandon(usage, "The assistant produced empty turns.");
        }

        return Abandon(usage, $"The interview did not converge within {MaxTurns} turns.");
    }

    /// <summary>
    /// One <c>brief_submit</c>: the accepted brief becomes the stage's outcome, an exhausted
    /// repair budget becomes a failure, and anything else returns <see langword="null"/> —
    /// the interview takes another turn carrying <paramref name="errors"/>.
    /// </summary>
    private static ForgeStageOutcome? HandleSubmission(
        ForgeSession session,
        ForgeEventWriter events,
        string json,
        ForgeUsageSnapshot usage,
        ref int submissionAttempts,
        ref IReadOnlyList<string>? errors)
    {
        if (ForgeBrief.TryParse(json, out var brief, out var briefErrors))
        {
            // Saved before the folders are confirmed, so a run stopped at that question resumes
            // there instead of re-interviewing; brief.ready waits for the confirmed list.
            session.SaveArtifact(ForgeSession.BriefFileName, brief!);
            session.Document.Title ??= Truncate(brief!.Goal!, 60);
            return new ForgeStageOutcome { Trigger = ForgeTrigger.BriefSubmitted, Usage = usage };
        }

        submissionAttempts++;
        events.Error(
            ForgeErrorCodes.BriefIncomplete,
            string.Join(" ", briefErrors),
            recoverable: submissionAttempts < MaxSubmissionAttempts);

        if (submissionAttempts >= MaxSubmissionAttempts)
            return Abandon(usage, $"No schema-valid brief after {MaxSubmissionAttempts} submissions.");

        errors = briefErrors;
        return null;
    }

    /// <summary>
    /// The folders step (STUDIO-46): proposes the brief's folders — or the defaults when the
    /// request named none — as <c>folders.proposed</c>, and waits for <c>folders.confirmed</c>.
    /// A list that breaks the rules, or binds a directory that is not there, is a recoverable
    /// <c>FORGE-FOLDERS-INVALID</c> and the proposal is made again. The confirmed list is kept
    /// in <c>folders.json</c> (with the directories bound) and in the brief (without them —
    /// a physical path never reaches a prompt), then announced by <c>brief.ready</c>.
    /// </summary>
    private async Task ConfirmFoldersAsync(
        ForgeSession session, ForgeEventWriter events, ForgeBrief brief, CancellationToken cancellationToken)
    {
        var proposal = ForgeFolders.ProposalOf(brief);
        while (true)
        {
            events.Emit("folders.proposed", new { folders = proposal });

            var answer = _autoConfirmFolders
                ? proposal
                : await _channel.ReadFoldersAsync(proposal, cancellationToken).ConfigureAwait(false)
                  ?? throw new OperationCanceledException("The user channel closed at the folders step.");

            var errors = new List<string>(ForgeFolders.Validate(answer));
            foreach (var folder in answer)
            {
                if (folder.Directory is { Length: > 0 } directory
                    && Path.IsPathFullyQualified(directory)
                    && !Directory.Exists(directory))
                {
                    errors.Add($"'{folder.Path}' is bound to a directory that does not exist.");
                }
            }

            if (errors.Count > 0)
            {
                events.Error(ForgeErrorCodes.FoldersInvalid, string.Join(" ", errors), recoverable: true);
                continue;
            }

            session.SaveArtifact(ForgeFolders.FileName, new ForgeFolderList { Folders = answer });
            var confirmed = brief with { Folders = [.. answer.Select(ForgeFolders.WithoutDirectory)] };
            session.SaveArtifact(ForgeSession.BriefFileName, confirmed);
            events.Emit("brief.ready", new { brief = confirmed });
            return;
        }
    }

    /// <summary>
    /// Shows the assistant's message and waits for the answer. A closed channel is an
    /// interruption, never an answer.
    /// </summary>
    private async Task<string> AskUserAsync(
        ForgeEventWriter events, string message, CancellationToken cancellationToken)
    {
        events.Emit("assistant.message", new { text = message });
        return await _channel.ReadUserMessageAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new OperationCanceledException("The user channel closed during the interview.");
    }

    /// <summary>The stage gives up: same failure code whatever exhausted the interview.</summary>
    private static ForgeStageOutcome Abandon(ForgeUsageSnapshot usage, string detail) => new()
    {
        Trigger = ForgeTrigger.Fail,
        Usage = usage,
        FailureCode = ForgeErrorCodes.BriefIncomplete,
        Detail = detail,
    };

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}

/// <summary>
/// The construction (SPEC-ORKEON-FORGE §7.3): from the brief — and any pending validation
/// errors — to a structurally valid blueprint, submitted through <c>blueprint_submit</c>.
/// Engine-initiated: no user round-trip happens here.
/// </summary>
internal sealed class BlueprintStage : IForgeStageRunner
{
    /// <summary>Schema-invalid submissions tolerated before the stage gives up.</summary>
    public const int MaxSubmissionAttempts = 3;

    private readonly IForgeAssistant _assistant;

    /// <summary>Builds the stage over the assistant seam.</summary>
    public BlueprintStage(IForgeAssistant assistant) =>
        _assistant = assistant ?? throw new ArgumentNullException(nameof(assistant));

    /// <inheritdoc />
    public ForgeState Stage => ForgeState.Blueprint;

    /// <inheritdoc />
    public async Task<ForgeStageOutcome> RunAsync(
        ForgeSession session, ForgeEventWriter events, CancellationToken cancellationToken)
    {
        var brief = session.TryLoadArtifact<ForgeBrief>(ForgeSession.BriefFileName);
        if (brief is null)
        {
            return new ForgeStageOutcome
            {
                Trigger = ForgeTrigger.Fail,
                FailureCode = ForgeErrorCodes.SessionCorrupt,
                Detail = $"'{ForgeSession.BriefFileName}' is missing: the session cannot build a blueprint without its brief.",
            };
        }

        var previous = session.TryLoadArtifact<ForgeBlueprint>(ForgeSession.BlueprintFileName);
        var errors = session.TryLoadArtifact<ForgeRepairState>(ForgeSession.RepairFileName)?.Errors;

        var usage = default(ForgeUsageSnapshot);
        for (var attempt = 1; attempt <= MaxSubmissionAttempts; attempt++)
        {
            // Same live meter as the interview: this phase has no user to answer, so its
            // one attempt is the longest silence of the whole cycle.
            var charged = usage;
            var reply = await _assistant.NextAsync(
                new ForgeAssistantRequest
                {
                    Phase = ForgeAssistantPhase.Blueprint,
                    Brief = brief,
                    PreviousBlueprint = previous,
                    Errors = errors,
                },
                partial => ForgeCost.Emit(events, session.Document.Budget, charged.Plus(partial)),
                cancellationToken).ConfigureAwait(false);
            usage = usage.Plus(reply.Usage);

            if (reply.BlueprintJson is { } json)
            {
                if (ForgeBlueprint.TryParse(json, out var blueprint, out var blueprintErrors))
                {
                    session.SaveArtifact(ForgeSession.BlueprintFileName, blueprint);
                    events.Emit("blueprint.ready", new { blueprint, iteration = session.Document.Iteration });
                    return new ForgeStageOutcome { Trigger = ForgeTrigger.BlueprintSubmitted, Usage = usage };
                }

                events.Error(
                    ForgeErrorCodes.BlueprintInvalid,
                    string.Join(" ", blueprintErrors),
                    recoverable: attempt < MaxSubmissionAttempts);
                errors = blueprintErrors;
                continue;
            }

            // Narration is passed through, but it costs an attempt: this phase has no user
            // to answer, so a chatty assistant must still converge on a submission.
            if (reply.Message is { Length: > 0 } message)
                events.Emit("assistant.message", new { text = message });
            errors = ["This phase expects a blueprint_submit call, not conversation."];
        }

        return new ForgeStageOutcome
        {
            Trigger = ForgeTrigger.Fail,
            Usage = usage,
            FailureCode = ForgeErrorCodes.BlueprintInvalid,
            Detail = $"No schema-valid blueprint after {MaxSubmissionAttempts} submissions.",
        };
    }
}

/// <summary>
/// The deterministic render (SPEC-ORKEON-FORGE §8.1): blueprint → per-entity YAML under the
/// session's <c>crew/</c>. No LLM anywhere.
/// </summary>
internal sealed class RenderStage : IForgeStageRunner
{
    /// <inheritdoc />
    public ForgeState Stage => ForgeState.Render;

    /// <inheritdoc />
    public Task<ForgeStageOutcome> RunAsync(
        ForgeSession session, ForgeEventWriter events, CancellationToken cancellationToken)
    {
        var blueprint = session.TryLoadArtifact<ForgeBlueprint>(ForgeSession.BlueprintFileName);
        if (blueprint is null)
        {
            return Task.FromResult(new ForgeStageOutcome
            {
                Trigger = ForgeTrigger.Fail,
                FailureCode = ForgeErrorCodes.SessionCorrupt,
                Detail = $"'{ForgeSession.BlueprintFileName}' is missing: nothing to render.",
            });
        }

        // One blueprint, two deterministic renders (SPEC §3.2) — the session's format
        // picks which one; nothing about the cycle changes around it.
        var written = ForgeSession.IsScriptFormat(session.Document.Format)
            ? ForgeScriptRenderer.Render(blueprint, session.Directory)
            : ForgeYamlRenderer.Render(ForgeBlueprintCompiler.Compile(blueprint, ForgeFolders.Of(session)), session.Directory);

        foreach (var path in written)
        {
            var bytes = new FileInfo(Path.Combine(session.Directory, path)).Length;
            events.Emit("file.written", new { path, bytes });
        }

        return Task.FromResult(new ForgeStageOutcome { Trigger = ForgeTrigger.Rendered });
    }
}

/// <summary>
/// The mechanical verdict (SPEC-ORKEON-FORGE §8.3-8.4): the shared validator plus the tool
/// check against the injected catalogue. Failure feeds the repair loop — two attempts, each
/// one evented, then the cycle surfaces the errors instead of burning budget silently.
/// </summary>
internal sealed class ValidateStage : IForgeStageRunner
{
    /// <summary>Repairs attempted before validation failure becomes final (SPEC §8.4).</summary>
    public const int MaxRepairAttempts = 2;

    private readonly IReadOnlyCollection<string> _knownTools;

    /// <summary>Builds the stage over the real tool catalogue, as <c>IToolRegistry</c> lists it.</summary>
    public ValidateStage(IReadOnlyCollection<string> knownTools) =>
        _knownTools = knownTools ?? throw new ArgumentNullException(nameof(knownTools));

    /// <inheritdoc />
    public ForgeState Stage => ForgeState.Validate;

    /// <inheritdoc />
    public Task<ForgeStageOutcome> RunAsync(
        ForgeSession session, ForgeEventWriter events, CancellationToken cancellationToken)
    {
        var blueprint = session.TryLoadArtifact<ForgeBlueprint>(ForgeSession.BlueprintFileName);
        if (blueprint is null)
        {
            return Task.FromResult(new ForgeStageOutcome
            {
                Trigger = ForgeTrigger.Fail,
                FailureCode = ForgeErrorCodes.SessionCorrupt,
                Detail = $"'{ForgeSession.BlueprintFileName}' is missing: nothing to validate.",
            });
        }

        var folders = ForgeFolders.Of(session);
        var compilation = ForgeBlueprintCompiler.Compile(blueprint, folders);
        var verdict = ForgeBlueprintCompiler.Validate(compilation, _knownTools, folders);
        var attempt = session.Document.RepairAttempts + 1;

        events.Emit("validation.result", new
        {
            ok = verdict.Errors.Count == 0,
            errors = verdict.Errors,
            warnings = verdict.Warnings,
            attempt,
        });

        if (verdict.Errors.Count == 0)
        {
            session.Document.RepairAttempts = 0;
            session.DeleteArtifact(ForgeSession.RepairFileName);
            return Task.FromResult(new ForgeStageOutcome { Trigger = ForgeTrigger.Validated });
        }

        session.Document.RepairAttempts = attempt;

        if (attempt > MaxRepairAttempts)
        {
            return Task.FromResult(new ForgeStageOutcome
            {
                Trigger = ForgeTrigger.Fail,
                FailureCode = ForgeErrorCodes.ValidationFailed,
                Detail = $"Still invalid after {MaxRepairAttempts} repairs: {string.Join(" ", verdict.Errors)}",
            });
        }

        session.SaveArtifact(ForgeSession.RepairFileName, new ForgeRepairState { Errors = verdict.Errors });
        events.Emit("repair.started", new { attempt, reason = "validation" });
        return Task.FromResult(new ForgeStageOutcome { Trigger = ForgeTrigger.RepairNeeded });
    }
}
