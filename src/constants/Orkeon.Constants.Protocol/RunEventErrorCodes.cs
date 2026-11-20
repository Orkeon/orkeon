namespace Orkeon.Constants.Protocol;

/// <summary>
/// The <c>code</c> an <see cref="RunEventKinds.Error"/> event carries when a run stops: whether it
/// was cancelled or failed. Spelled once, like the kinds: the CLI writes them on the wire
/// (<c>orkeon run --events jsonl</c>), a C# host reads them on the events of
/// <c>ICrewOrchestrationService.KickoffStreamingAsync</c> (GAP-32), and an external client reads the wire.
/// </summary>
public static class RunEventErrorCodes
{
    /// <summary>The run failed: a task did not succeed, or the run stopped on an error.</summary>
    public const string CrewFailed = "crew_failed";

    /// <summary>The run was cancelled — a timeout, Ctrl+C, a consumer that left the stream.</summary>
    public const string CrewCancelled = "crew_cancelled";

    /// <summary>
    /// The run was refused before its first task: the crew requires System One (<c>decision.mode: required</c>)
    /// and the decision circuit is off. The message names the crew and the cause (#429).
    /// </summary>
    public const string DecisionUnavailable = "decision_unavailable";
}
