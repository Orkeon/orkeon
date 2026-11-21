namespace Orkeon.Constants.Cli;

/// <summary>The exit codes <c>orkeon run</c> adds to 0, 1, 2 and 130 (the daemon's own is 78).</summary>
public static class RunExitCodes
{
    /// <summary>
    /// The run stopped on a question to a human and waits for the answer (<c>EX_TEMPFAIL</c>): resume it with
    /// <c>orkeon run &lt;crew&gt; --resume &lt;id&gt; --input "..."</c>. Neither a success nor a failure (FWK-23).
    /// </summary>
    public const int WaitingForInput = 75;

    /// <summary>
    /// The run was refused before its first task (<c>EX_UNAVAILABLE</c>): the crew requires System One
    /// (<c>decision.mode: required</c>) and the decision circuit is off - <c>--no-decision</c>,
    /// <c>Orkeon:Decision:Enabled=false</c>, or no decision provider resolved (#429). An <c>optional</c> crew
    /// resumed under the other circuit than the one its recorded decisions were taken with exits 1 instead, code
    /// <c>decision_circuit_mismatch</c> (#430).
    /// </summary>
    public const int DecisionUnavailable = 69;
}
