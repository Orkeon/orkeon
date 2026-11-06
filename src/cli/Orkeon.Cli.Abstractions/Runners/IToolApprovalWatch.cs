namespace Orkeon.Cli.Abstractions.Runners;

/// <summary>
/// The tool approvals waiting for the operator, read by the views that must show one wherever the operator looks:
/// the status line and the tasks list of the TUI. Implemented by the REPL's approval channel.
/// </summary>
public interface IToolApprovalWatch
{
    /// <summary>The questions waiting, the one the next answer settles first.</summary>
    IReadOnlyList<PendingToolApproval> Pending { get; }

    /// <summary>
    /// Raised after a question is asked, answered or withdrawn, outside any lock: a replay of the REPL waits on it
    /// rather than polling <see cref="Pending"/>.
    /// </summary>
    event EventHandler? PendingChanged;

    /// <summary>
    /// Answers the question <paramref name="id"/> and no other, for a view that shows it: nothing goes through the
    /// console's lines, so a read open elsewhere never receives the answer.
    /// </summary>
    /// <param name="id">The <see cref="PendingToolApproval.Id"/> of the question shown.</param>
    /// <param name="answer"><c>y</c>, <c>s</c> or <c>n</c>.</param>
    /// <returns>False when the question no longer waits first, or the answer is none of these: nothing is settled.</returns>
    bool TryAnswer(long id, string answer) => false;
}

/// <summary>One tool approval waiting for the operator.</summary>
/// <param name="ToolName">The tool the call names.</param>
/// <param name="Arguments">The call's arguments as the question shows them, JSON cut at 300 characters.</param>
/// <param name="Ticket">The ticket of the background task that asks (<c>t1</c>); null outside a task.</param>
/// <param name="TaskName">The name of that task (<c>dev-batches</c>); null outside a task.</param>
public sealed record PendingToolApproval(string ToolName, string Arguments, string? Ticket, string? TaskName)
{
    /// <summary>
    /// The question's identity within its source, the same at every read of <see cref="IToolApprovalWatch.Pending"/>:
    /// two questions alike in every other field still differ by it. 0 when the source gives none.
    /// </summary>
    public long Id { get; init; }

    /// <summary>Why the gate asks, when it says.</summary>
    public string? Reason { get; init; }
}
