using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.Input;

namespace Orkeon.Cli.TerminalGui.Tests.Doubles;

/// <summary>A prompt that only states its recipient, for a list tested without a prompt.</summary>
internal sealed class StubTaskListNavigator : ITaskListNavigator
{
    public string? RecipientTicket { get; init; }

    public bool HandleListKey(Key key) => false;

    public void EnterList()
    {
    }

    public void LeaveList()
    {
    }

    public void SelectRow(string? ticket)
    {
    }

    public void RowsRefreshed(IReadOnlyList<AgentRowInfo> rows)
    {
    }
}
