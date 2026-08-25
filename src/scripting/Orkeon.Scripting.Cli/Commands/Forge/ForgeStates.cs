namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// The stages of the forge cycle (SPEC-ORKEON-FORGE §4). The user-facing milestones are a
/// client-side projection of these — the engine never exposes fewer states than it has.
/// </summary>
internal enum ForgeState
{
    /// <summary>The interview: the assistant extracts a structured brief.</summary>
    Brief,

    /// <summary>The team plan: agents, tasks, orchestration, rationale.</summary>
    Blueprint,

    /// <summary>Deterministic serialization of the blueprint (YAML and/or script).</summary>
    Render,

    /// <summary>Mechanical validation of the rendered crew, with its repair loop.</summary>
    Validate,

    /// <summary>Sandboxed execution on the brief's sample input.</summary>
    Test,

    /// <summary>Judgement of the run against the brief's acceptance criteria.</summary>
    Diagnose,

    /// <summary>The arbitration: accept, refine, or stop.</summary>
    Verdict,

    /// <summary>A conforming crew, ready to be promoted.</summary>
    Ready,

    /// <summary>Promoted out of the session directory. Terminal.</summary>
    Promoted,

    /// <summary>The user stopped the cycle. Terminal.</summary>
    Abandoned,

    /// <summary>An unrecoverable engine error. Terminal.</summary>
    Failed,
}

/// <summary>What moves the cycle from one stage to the next.</summary>
internal enum ForgeTrigger
{
    /// <summary>The interview produced a schema-valid brief (<c>brief_submit</c>).</summary>
    BriefSubmitted,

    /// <summary>The assistant produced a schema-valid blueprint (<c>blueprint_submit</c>).</summary>
    BlueprintSubmitted,

    /// <summary>The crew files were written.</summary>
    Rendered,

    /// <summary>The rendered crew passed <c>CrewDefinitionValidator</c> and the tool check.</summary>
    Validated,

    /// <summary>Validation failed and a repair attempt goes back through the blueprint.</summary>
    RepairNeeded,

    /// <summary>The sandboxed run finished — success or failure, the diagnosis runs either way.</summary>
    TestCompleted,

    /// <summary>The judge produced its verdict payload.</summary>
    Diagnosed,

    /// <summary>The verdict is conforming, or the user accepted it.</summary>
    Accepted,

    /// <summary>The user (or <c>--auto</c>) asked for another cycle with the diagnosis folded in.</summary>
    RefineRequested,

    /// <summary>
    /// The user asked to re-run the trial as-is (<c>decision.made {retry}</c>) — same
    /// blueprint, same render, a fresh run and a fresh verdict. Zero LLM compose.
    /// </summary>
    RetryRequested,

    /// <summary>
    /// A promoted (or abandoned-with-verdict) session was resumed for the modify /
    /// re-try / re-adopt cycle (W-09). Applied at the command level — the machine keeps
    /// its terminal states terminal; this trigger only names the history entry.
    /// </summary>
    Reopen,

    /// <summary>
    /// The user handed back an amended blueprint at the arbitration (<c>decision.made
    /// {edit}</c> + <c>blueprint.edited</c>). Re-render without an LLM turn.
    /// </summary>
    BlueprintEdited,

    /// <summary>The user stopped the cycle.</summary>
    Abandon,

    /// <summary>The ready crew was promoted.</summary>
    Promote,

    /// <summary>An unrecoverable engine error.</summary>
    Fail,
}
