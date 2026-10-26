using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.Cli.Abstractions.Tests.Doubles;

/// <summary>
/// Hand-written double for <see cref="IToolApprovalWatch"/>: serves the approvals the test puts in
/// <see cref="Pending"/>, read afresh at each call; <see cref="Ask"/> adds one and signals it, as the approval
/// channel does.
/// </summary>
public sealed class FakeToolApprovalWatch : IToolApprovalWatch
{
    private long _nextId;

    /// <summary>The approvals served as waiting, in order.</summary>
    public List<PendingToolApproval> Pending { get; } = [];

    IReadOnlyList<PendingToolApproval> IToolApprovalWatch.Pending => Pending;

    /// <inheritdoc />
    public event EventHandler? PendingChanged;

    /// <summary>Adds a question waiting for <paramref name="toolName"/> and raises <see cref="PendingChanged"/>.</summary>
    /// <returns>The question added, with an <see cref="PendingToolApproval.Id"/> of its own.</returns>
    public PendingToolApproval Ask(string toolName)
    {
        var question = new PendingToolApproval(toolName, "{}", Ticket: null, TaskName: null) { Id = ++_nextId };
        Pending.Add(question);
        NotifyPendingChanged();
        return question;
    }

    /// <summary>Removes <paramref name="question"/> (answered or withdrawn) and raises <see cref="PendingChanged"/>.</summary>
    public void Settle(PendingToolApproval question)
    {
        Pending.Remove(question);
        NotifyPendingChanged();
    }

    /// <summary>Signals a change of <see cref="Pending"/>, as the approval channel does.</summary>
    public void NotifyPendingChanged() => PendingChanged?.Invoke(this, EventArgs.Empty);
}
