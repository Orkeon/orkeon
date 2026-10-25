namespace Orkeon.Cli.Abstractions.Runners;

/// <summary>
/// The tool approvals waiting for the operator, read by the views that must show one wherever the operator looks:
/// the status line and the tasks list of the TUI. Implemented by the REPL's approval channel.
/// </summary>
public interface IToolApprovalWatch
{
    /// <summary>The questions waiting, the one the next answer settles first.</summary>
    IReadOnlyList<PendingToolApproval> Pending { get; }
}

/// <summary>One tool approval waiting for the operator.</summary>
/// <param name="ToolName">The tool the call names.</param>
/// <param name="Arguments">The call's arguments as the question shows them, JSON cut at 300 characters.</param>
/// <param name="Ticket">The ticket of the background task that asks (<c>t1</c>); null outside a task.</param>
/// <param name="TaskName">The name of that task (<c>dev-batches</c>); null outside a task.</param>
public sealed record PendingToolApproval(string ToolName, string Arguments, string? Ticket, string? TaskName);
