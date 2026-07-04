using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

// TUI-06.1: capture only, no navigation yet — tracked
/// <summary>
/// Modal dialog used by Ctrl+F to search the logs pane. v1: single Find-next on dismissal.
/// </summary>
public sealed class FindDialog : Window
{
    private readonly TextField _input;
    private string _lastQuery = string.Empty;

    public FindDialog(LogsPaneView logs)
    {
        // The logs pane is the eventual Find target (TUI-06.1). Validated here even
        // though the v1 dialog only captures the query; the reference is not yet stored.
        ArgumentNullException.ThrowIfNull(logs);
        Title = "Find in logs";
        Width = 60;
        Height = 6;
        X = Pos.Center();
        Y = Pos.Center();

        var label = new Label { X = 1, Y = 1, Text = "Search:" };
        _input = new TextField { X = Pos.Right(label) + 1, Y = 1, Width = Dim.Fill(2) };
        var find = new Button { X = 1, Y = 3, Text = "Find next" };
        var close = new Button { X = Pos.Right(find) + 2, Y = 3, Text = "Close" };

        find.Accepting += (_, _) => DoFind();
        close.Accepting += (_, _) => Application.RequestStop(this);

        Add(label, _input, find, close);
    }

    public void Show() => Application.Run(this);

    private void DoFind()
    {
        _lastQuery = _input.Text ?? string.Empty;
        // v1: TextView.FindNextText is invoked by a follow-up task (TUI-06.1).
        // For now we simply close after capturing the query.
        Application.RequestStop(this);
    }

    /// <summary>Test-only accessor for the last query value (set when a Find action is invoked).</summary>
    internal string LastQuery => _lastQuery;

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Terminal.Gui's base View.Dispose also disposes the Add()-ed _input; its
            // IsDisposed guard makes this explicit call an idempotent no-op.
            _input.Dispose();
        }
        base.Dispose(disposing);
    }
}
