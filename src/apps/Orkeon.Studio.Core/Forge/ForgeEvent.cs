namespace Orkeon.Studio.Core.Forge;

/// <summary>
/// Wire spellings of the forge event protocol (SPEC-ORKEON-FORGE §6). Re-declared here on
/// purpose: Studio.Core never references the CLI (dependency diet), so the protocol shape
/// is the contract — pinned against the CLI's golden lines by
/// <c>ForgeEventParserTests</c>, exactly like the doctor JSON contract.
/// </summary>
public static class ForgeEventKinds
{
    /// <summary>
    /// Opening event of an engine run: <c>slug</c>, <c>id</c> — the session's stable id, which
    /// its team's <c>forge.json</c> carries once promoted (STUDIO-25) — <c>dir</c>,
    /// <c>format</c>, <c>resumed</c>, <c>engine</c> and <c>budget</c>.
    /// </summary>
    public const string SessionStarted = "session.started";

    /// <summary>The cycle entered a stage.</summary>
    public const string StageEntered = "stage.entered";

    /// <summary>One turn of the assistant — the conversation itself.</summary>
    public const string AssistantMessage = "assistant.message";

    /// <summary>A closed question from the engine.</summary>
    public const string QuestionAsked = "question.asked";

    /// <summary>The structured brief is available.</summary>
    public const string BriefReady = "brief.ready";

    /// <summary>A team plan was proposed.</summary>
    public const string BlueprintReady = "blueprint.ready";

    /// <summary>One crew file was written.</summary>
    public const string FileWritten = "file.written";

    /// <summary>Validation verdict of the rendered crew.</summary>
    public const string ValidationResult = "validation.result";

    /// <summary>A repair loop started.</summary>
    public const string RepairStarted = "repair.started";

    /// <summary>The sandboxed run started.</summary>
    public const string RunStarted = "run.started";

    /// <summary>One task of the run completed.</summary>
    public const string TaskCompleted = "task.completed";

    /// <summary>The token meter moved.</summary>
    public const string CostUpdated = "cost.updated";

    /// <summary>The sandboxed run finished.</summary>
    public const string RunFinished = "run.finished";

    /// <summary>The diagnosis produced its verdict.</summary>
    public const string VerdictReady = "verdict.ready";

    /// <summary>The engine waits for a human arbitration.</summary>
    public const string DecisionNeeded = "decision.needed";

    /// <summary>The session was promoted to an ordinary folder.</summary>
    public const string Promoted = "promoted";

    /// <summary>
    /// Once a promotion is written, the session folder took the team folder's name (STUDIO-26):
    /// <c>from</c> and <c>to</c> are the session's slugs, <c>dir</c> its new folder, and
    /// <c>suffixed</c> says the team's own name was already another session's, so a <c>-2</c>…
    /// was added. The session's id does not change: rule R still links it to its team.
    /// </summary>
    public const string SessionRenamed = "session.renamed";

    /// <summary>
    /// Something the command could not do while everything it was asked for stands: <c>code</c>
    /// (<c>FORGE-…</c>) and <c>message</c> — a session folder the disk would not rename after a
    /// written promotion (STUDIO-26, D-05). Never an error: the run's outcome is its exit code.
    /// </summary>
    public const string Warning = "warning";

    /// <summary>Closing event; mirrors the process exit code.</summary>
    public const string SessionFinished = "session.finished";

    /// <summary>
    /// Where a team folder's schedule stands after <c>forge schedule</c>, <c>--check</c> or
    /// <c>forge unschedule</c> (STUDIO-27): <c>path</c>, <c>state</c> (<c>installed</c> |
    /// <c>absent</c> | <c>stale</c>), <c>reason</c>, <c>expression</c>, <c>family</c>,
    /// <c>names</c> and, for a removal, <c>removed</c>. A refusal is an <c>error</c> instead, whose
    /// <c>command</c> is what a person can run by hand.
    /// </summary>
    public const string ScheduleState = "schedule.state";

    /// <summary>
    /// <c>forge reopen</c> found or rebuilt the session of a promoted team folder (FORGE-09):
    /// <c>slug</c>, <c>dir</c>, <c>path</c>, the wire <c>state</c> of the session,
    /// <c>rebuilt</c>, and for a rebuild whether the brief was <c>recorded</c> or <c>derived</c>.
    /// </summary>
    public const string TeamReopened = "team.reopened";

    /// <summary>
    /// <c>forge rename</c> renamed a team (STUDIO-28): <c>from</c> is the folder it had,
    /// <c>path</c> the folder it has now, <c>name</c> the name every title carries. A
    /// <c>session.renamed</c> precedes it when the linked session's folder followed, a
    /// <c>schedule.state</c> when the schedule was reinstalled under the new name. A refusal is an
    /// <c>error</c> instead — and then nothing changed.
    /// </summary>
    public const string TeamRenamed = "team.renamed";

    /// <summary>An anomaly, recoverable or not.</summary>
    public const string Error = "error";

    /// <summary>Inbound: the user's next conversation turn (stdin).</summary>
    public const string UserMessage = "user.message";

    /// <summary>Inbound: the user's arbitration (stdin).</summary>
    public const string DecisionMade = "decision.made";

    /// <summary>Inbound: the amended blueprint that follows a <c>decision.made {edit}</c> (stdin).</summary>
    public const string BlueprintEdited = "blueprint.edited";
}
