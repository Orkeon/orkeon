using Orkeon.Cli.TerminalGui.Hosting;
using Terminal.Gui.Input;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// The prompt side of the tasks list: the list hands its keys, its F4 requests and its refreshed
/// rows to the navigation machine the REPL pane owns, and applies what comes back.
/// </summary>
internal interface ITaskListNavigator
{
    /// <summary>A key on the focused list; true when the machine consumed it.</summary>
    bool HandleListKey(Key key);

    /// <summary>F4 asked for the list.</summary>
    void EnterList();

    /// <summary>F4 (or the host) asked to leave the list.</summary>
    void LeaveList();

    /// <summary>The 1 Hz poll replaced the rows.</summary>
    void RowsRefreshed(IReadOnlyList<AgentRowInfo> rows);
}
