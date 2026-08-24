namespace Orkeon.Studio.Core.Forge;

/// <summary>
/// Wire spellings of the forge event protocol (SPEC-ORKEON-FORGE §6). Re-declared here on
/// purpose: Studio.Core never references the CLI (dependency diet), so the protocol shape
/// is the contract — pinned against the CLI's golden lines by
/// <c>ForgeEventParserTests</c>, exactly like the doctor JSON contract.
/// </summary>
public static class ForgeEventKinds
{
    /// <summary>Opening event of an engine run.</summary>
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

    /// <summary>Closing event; mirrors the process exit code.</summary>
    public const string SessionFinished = "session.finished";

    /// <summary>An anomaly, recoverable or not.</summary>
    public const string Error = "error";

    /// <summary>Inbound: the user's next conversation turn (stdin).</summary>
    public const string UserMessage = "user.message";

    /// <summary>Inbound: the user's arbitration (stdin).</summary>
    public const string DecisionMade = "decision.made";

    /// <summary>Inbound: the amended blueprint that follows a <c>decision.made {edit}</c> (stdin).</summary>
    public const string BlueprintEdited = "blueprint.edited";
}
