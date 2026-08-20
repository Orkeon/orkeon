namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>Which deliverable the assistant is being asked to work toward.</summary>
internal enum ForgeAssistantPhase
{
    /// <summary>The interview: converge on a schema-valid brief.</summary>
    Brief,

    /// <summary>The construction: turn the brief (and any repair errors) into a blueprint.</summary>
    Blueprint,
}

/// <summary>Everything one assistant turn may need. Null fields simply do not apply.</summary>
internal sealed record ForgeAssistantRequest
{
    /// <summary>The deliverable being worked toward.</summary>
    public required ForgeAssistantPhase Phase { get; init; }

    /// <summary>The user's message for this turn; null on an engine-initiated turn.</summary>
    public string? UserMessage { get; init; }

    /// <summary>The accepted brief, once there is one (Blueprint phase).</summary>
    public ForgeBrief? Brief { get; init; }

    /// <summary>The previous blueprint, when this turn repairs or refines one.</summary>
    public ForgeBlueprint? PreviousBlueprint { get; init; }

    /// <summary>
    /// The errors the previous submission earned, verbatim from the validator — the repair
    /// prompt carries them untranslated (SPEC §8.4).
    /// </summary>
    public IReadOnlyList<string>? Errors { get; init; }
}

/// <summary>
/// What one assistant turn produced: conversation text, or a submission — never both
/// interpreted at once. A submission wins when present.
/// </summary>
internal sealed record ForgeAssistantReply
{
    /// <summary>Conversation text (an <c>assistant.message</c> event); null when the turn submitted.</summary>
    public string? Message { get; init; }

    /// <summary>The raw JSON handed to <c>brief_submit</c>, when the turn submitted a brief.</summary>
    public string? BriefJson { get; init; }

    /// <summary>The raw JSON handed to <c>blueprint_submit</c>, when the turn submitted a blueprint.</summary>
    public string? BlueprintJson { get; init; }

    /// <summary>LLM tokens the turn consumed, charged to the session budget.</summary>
    public long TokensConsumed { get; init; }
}

/// <summary>
/// The conversational side of the cycle (SPEC-ORKEON-FORGE §7.1) behind one seam. The
/// production implementation is the exp07-adapted crew run in-engine via
/// <c>ScriptHost</c>; the tests script this interface directly — no test talks to a model,
/// and the stages cannot tell the difference. Submissions come back as raw JSON: parsing
/// and schema validation stay on the engine side of the seam, never trusted to the
/// assistant (§3.3 — the crew never steers the cycle).
/// </summary>
internal interface IForgeAssistant
{
    /// <summary>Runs one turn.</summary>
    Task<ForgeAssistantReply> NextAsync(ForgeAssistantRequest request, CancellationToken cancellationToken);
}
