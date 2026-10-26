using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.Cli.Abstractions.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IToolApprovalWatch"/>: serves the approvals the test puts in
/// <see cref="Pending"/>, read afresh at each call; <see cref="Ask"/> adds one and signals it, as the approval
/// channel does.
/// </summary>
public sealed class FakeToolApprovalWatch : IToolApprovalWatch
{
    /// <summary>The approvals served as waiting, in order.</summary>
    public List<PendingToolApproval> Pending { get; } = [];

    IReadOnlyList<PendingToolApproval> IToolApprovalWatch.Pending => Pending;

    /// <inheritdoc />
    public event EventHandler? PendingChanged;

    /// <summary>Adds a question waiting for <paramref name="toolName"/> and raises <see cref="PendingChanged"/>.</summary>
    public void Ask(string toolName)
    {
        Pending.Add(new PendingToolApproval(toolName, "{}", Ticket: null, TaskName: null));
        NotifyPendingChanged();
    }

    /// <summary>Signals a change of <see cref="Pending"/>, as the approval channel does.</summary>
    public void NotifyPendingChanged() => PendingChanged?.Invoke(this, EventArgs.Empty);
}
