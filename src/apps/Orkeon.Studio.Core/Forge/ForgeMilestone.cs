namespace Orkeon.Studio.Core.Forge;

/// <summary>
/// The four user-facing milestones (UX study §3): Décrire ▸ Proposer ▸ Essayer ▸ Adopter.
/// A client-side projection — the engine has more states than the user has milestones,
/// and that asymmetry is the point.
/// </summary>
public enum ForgeMilestone
{
    /// <summary>The interview: the conversation and the "success" card filling up.</summary>
    Describe,

    /// <summary>The proposal: blueprint, render and validation — repairs invisible unless final.</summary>
    Propose,

    /// <summary>The try: sandboxed run, diagnosis, arbitration.</summary>
    Try,

    /// <summary>"And now?": the crew is ready or promoted.</summary>
    Adopt,
}

/// <summary>The stage → milestone table of the UX study §3, verbatim.</summary>
public static class ForgeMilestones
{
    /// <summary>
    /// Maps a wire stage name (<c>stage.entered</c>'s lowercase spelling) to its milestone;
    /// null for terminal failure states, which are not milestones — the client keeps the
    /// last one and shows the error instead. <c>Refine</c> is not a stage: a refine re-enters
    /// <c>blueprint</c>, which lands back on <see cref="ForgeMilestone.Propose"/> by this
    /// same table.
    /// </summary>
    public static ForgeMilestone? FromStage(string? stage) => stage switch
    {
        "brief" => ForgeMilestone.Describe,
        "blueprint" or "render" or "validate" => ForgeMilestone.Propose,
        "test" or "diagnose" or "verdict" => ForgeMilestone.Try,
        "ready" or "promoted" => ForgeMilestone.Adopt,
        _ => null,
    };
}
