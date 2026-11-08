namespace Orkeon.Constants.Cli;

/// <summary>The exit codes <c>orkeon run</c> adds to 0, 1, 2 and 130 (the daemon's own is 78).</summary>
public static class RunExitCodes
{
    /// <summary>
    /// The run stopped on a question to a human and waits for the answer (<c>EX_TEMPFAIL</c>): resume it with
    /// <c>orkeon run &lt;crew&gt; --resume &lt;id&gt; --input "..."</c>. Neither a success nor a failure (FWK-23).
    /// </summary>
    public const int WaitingForInput = 75;
}
