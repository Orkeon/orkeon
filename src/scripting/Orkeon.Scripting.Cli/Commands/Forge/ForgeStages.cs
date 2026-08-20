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

    /// <summary>Builds the stage; <paramref name="initialNeed"/> is the need typed on the command line, when any.</summary>
    public BriefStage(IForgeAssistant assistant, IForgeUserChannel channel, string? initialNeed = null)
    {
        _assistant = assistant ?? throw new ArgumentNullException(nameof(assistant));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _initialNeed = initialNeed;
    }

    /// <inheritdoc />
    public ForgeState Stage => ForgeState.Brief;

    /// <inheritdoc />
    public async Task<ForgeStageOutcome> RunAsync(
        ForgeSession session, ForgeEventWriter events, CancellationToken cancellationToken)
    {
        // A resumed session that already interviewed does not re-interview.
        if (session.TryLoadArtifact<ForgeBrief>(ForgeSession.BriefFileName) is not null)
            return new ForgeStageOutcome { Trigger = ForgeTrigger.BriefSubmitted };

        long tokens = 0;
        var submissionAttempts = 0;
        var userMessage = _initialNeed;
        IReadOnlyList<string>? errors = null;

        for (var turn = 0; turn < MaxTurns; turn++)
        {
            var reply = await _assistant.NextAsync(
                new ForgeAssistantRequest { Phase = ForgeAssistantPhase.Brief, UserMessage = userMessage, Errors = errors },
                cancellationToken).ConfigureAwait(false);
            tokens += reply.TokensConsumed;

            if (reply.BriefJson is { } json)
            {
                if (ForgeBrief.TryParse(json, out var brief, out var briefErrors))
                {
                    session.SaveArtifact(ForgeSession.BriefFileName, brief);
                    session.Document.Title ??= Truncate(brief!.Goal!, 60);
                    events.Emit("brief.ready", new { brief });
                    return new ForgeStageOutcome { Trigger = ForgeTrigger.BriefSubmitted, TokensConsumed = tokens };
                }

                submissionAttempts++;
                events.Error(
                    ForgeErrorCodes.BriefIncomplete,
                    string.Join(" ", briefErrors),
                    recoverable: submissionAttempts < MaxSubmissionAttempts);

                if (submissionAttempts >= MaxSubmissionAttempts)
                {
                    return new ForgeStageOutcome
                    {
                        Trigger = ForgeTrigger.Fail,
                        TokensConsumed = tokens,
                        FailureCode = ForgeErrorCodes.BriefIncomplete,
                        Detail = $"No schema-valid brief after {MaxSubmissionAttempts} submissions.",
                    };
                }

                // The errors go back into the next turn, verbatim; no user round-trip needed.
                userMessage = null;
                errors = briefErrors;
                continue;
            }

            if (reply.Message is { Length: > 0 } message)
            {
                events.Emit("assistant.message", new { text = message });
                userMessage = await _channel.ReadUserMessageAsync(cancellationToken).ConfigureAwait(false);
                if (userMessage is null)
                    throw new OperationCanceledException("The user channel closed during the interview.");

                errors = null;
                continue;
            }

            // Neither a message nor a submission: a broken turn.
            userMessage = null;
            errors = ["The turn produced neither a message nor a submission."];
            submissionAttempts++;
            if (submissionAttempts >= MaxSubmissionAttempts)
            {
                return new ForgeStageOutcome
                {
                    Trigger = ForgeTrigger.Fail,
                    TokensConsumed = tokens,
                    FailureCode = ForgeErrorCodes.BriefIncomplete,
                    Detail = "The assistant produced empty turns.",
                };
            }
        }

        return new ForgeStageOutcome
        {
            Trigger = ForgeTrigger.Fail,
            TokensConsumed = tokens,
            FailureCode = ForgeErrorCodes.BriefIncomplete,
            Detail = $"The interview did not converge within {MaxTurns} turns.",
        };
    }

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

        long tokens = 0;
        for (var attempt = 1; attempt <= MaxSubmissionAttempts; attempt++)
        {
            var reply = await _assistant.NextAsync(
                new ForgeAssistantRequest
                {
                    Phase = ForgeAssistantPhase.Blueprint,
                    Brief = brief,
                    PreviousBlueprint = previous,
                    Errors = errors,
                },
                cancellationToken).ConfigureAwait(false);
            tokens += reply.TokensConsumed;

            if (reply.BlueprintJson is { } json)
            {
                if (ForgeBlueprint.TryParse(json, out var blueprint, out var blueprintErrors))
                {
                    session.SaveArtifact(ForgeSession.BlueprintFileName, blueprint);
                    events.Emit("blueprint.ready", new { blueprint, iteration = session.Document.Iteration });
                    return new ForgeStageOutcome { Trigger = ForgeTrigger.BlueprintSubmitted, TokensConsumed = tokens };
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
            TokensConsumed = tokens,
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
            : ForgeYamlRenderer.Render(ForgeBlueprintCompiler.Compile(blueprint), session.Directory);

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

        var compilation = ForgeBlueprintCompiler.Compile(blueprint);
        var verdict = ForgeBlueprintCompiler.Validate(compilation, _knownTools);
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
