using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.Cli.Commands.Scripting.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IToolApprovalWatch"/>: serves the approvals the test puts in
/// <see cref="Pending"/>, read afresh at each call.
/// </summary>
public sealed class FakeToolApprovalWatch : IToolApprovalWatch
{
    /// <summary>The approvals served as waiting, in order.</summary>
    public List<PendingToolApproval> Pending { get; } = [];

    IReadOnlyList<PendingToolApproval> IToolApprovalWatch.Pending => Pending;

    /// <inheritdoc />
    public event EventHandler? PendingChanged;

    /// <summary>Signals a change of <see cref="Pending"/>, as the approval channel does.</summary>
    public void NotifyPendingChanged() => PendingChanged?.Invoke(this, EventArgs.Empty);
}
