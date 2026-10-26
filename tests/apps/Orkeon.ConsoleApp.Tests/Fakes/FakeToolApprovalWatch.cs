using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>The approvals waiting, set by the test; <see cref="Ask"/> raises <see cref="PendingChanged"/>.</summary>
internal sealed class FakeToolApprovalWatch : IToolApprovalWatch
{
    public List<PendingToolApproval> Questions { get; } = [];

    public IReadOnlyList<PendingToolApproval> Pending => Questions;

    public event EventHandler? PendingChanged;

    /// <summary>Adds a question waiting and signals it, as the approval channel does.</summary>
    public void Ask(string toolName)
    {
        Questions.Add(new PendingToolApproval(toolName, "{}", Ticket: null, TaskName: null));
        PendingChanged?.Invoke(this, EventArgs.Empty);
    }
}
