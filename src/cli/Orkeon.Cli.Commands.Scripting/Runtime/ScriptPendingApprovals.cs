using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.Cli.Commands.Scripting.Runtime;

#pragma warning disable IDE1006 // camelCase members: these CLR types are exposed to scripts as ctx.services.get("approvals")

/// <summary>
/// The tool approvals waiting for the operator, as a command reads them: <c>ctx.services.get("approvals")</c>.
/// Read-only: an approval is answered by the operator, never by a script. Each <see cref="list"/> reads the
/// questions waiting at that moment, so a task blocked on the operator can be told from one that runs.
/// </summary>
public sealed class ScriptPendingApprovals
{
    private readonly IToolApprovalWatch _watch;

    internal ScriptPendingApprovals(IToolApprovalWatch watch)
    {
        ArgumentNullException.ThrowIfNull(watch);
        _watch = watch;
    }

    /// <summary>The approvals waiting, the one the next answer settles first; empty when none waits.</summary>
    public ScriptPendingApproval[] list() => [.. _watch.Pending.Select(ScriptPendingApproval.From)];
}

/// <summary>One tool approval waiting for the operator, as <see cref="ScriptPendingApprovals.list"/> returns it.</summary>
public sealed class ScriptPendingApproval
{
    /// <summary>The ticket of the background task that asks (<c>t1</c>); null outside a task.</summary>
    public string? taskId { get; init; }

    /// <summary>The name of the command that runs that task (<c>dev-batches</c>); null outside a task.</summary>
    public string? command { get; init; }

    /// <summary>The tool the call names (<c>shell_command</c>).</summary>
    public required string tool { get; init; }

    /// <summary>The call's input as the status line shows it, cut at <see cref="PendingToolApprovalInput.MaxLength"/> characters.</summary>
    public required string input { get; init; }

    internal static ScriptPendingApproval From(PendingToolApproval approval) => new()
    {
        taskId = approval.Ticket,
        command = approval.TaskName,
        tool = approval.ToolName,
        input = PendingToolApprovalInput.Summarize(approval),
    };
}
#pragma warning restore IDE1006
