using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Tests.Doubles;

namespace Orkeon.Cli.TerminalGui.Tests.Fixtures;

/// <summary>A pane bound to approvals a test asks and withdraws, recording what the pane answers.</summary>
internal sealed class ApprovalPaneFixture : IDisposable
{
    private readonly List<PendingToolApproval> _pending = [];
    private readonly List<(long Id, string Answer)> _answers = [];

    public ApprovalPaneFixture()
    {
        Pane = new ReplPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);
        Pane.BindIntegration(new TuiIntegration
        {
            PendingApprovals = () => _pending,
            AnswerApproval = (id, answer) =>
            {
                _answers.Add((id, answer));
                return true;
            },
        });
    }

    public ReplPaneView Pane { get; }

    /// <summary>What the pane answered, in order.</summary>
    public IReadOnlyList<(long Id, string Answer)> Answers => _answers;

    /// <summary>Adds a question to the queue; the pane hears of it at its next <c>RaiseApprovalsChanged</c>.</summary>
    public void Ask(long id) => _pending.Add(Approval(id));

    /// <summary>Puts another question in place of the first one.</summary>
    public void ReplaceQuestion(long id) => _pending[0] = Approval(id);

    /// <summary>Empties the queue.</summary>
    public void WithdrawQuestions() => _pending.Clear();

    public void Dispose() => Pane.Dispose();

    private static PendingToolApproval Approval(long id) => new("run_command", "{}", null, null) { Id = id };
}
