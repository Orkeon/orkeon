using Orkeon.Cli.TerminalGui.Hosting;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// The agents list under the hint bar (PLAN C9): one row per in-flight or recent
/// async command instance, plus <c>main</c>. Rows come from the host through
/// <see cref="TuiIntegration.AgentRows"/> and are re-polled at 1 Hz — the command
/// registry emits no change events, so polling is the mechanism, same as the
/// pre-fidelity bandeau.
/// </summary>
public sealed class AgentsPaneView : View
{
    /// <summary>Cap so a burst of tickets cannot swallow the transcript.</summary>
    internal const int MaxRows = 6;

    private readonly MouseClipboardTextView _text;
    private readonly IUiDispatcher _dispatcher;
    private readonly GlyphSet _glyphs;
    private TuiIntegration? _integration;
    private object? _timerToken;
    private int _desiredRows;

    public AgentsPaneView(TerminalGuiOptions options)
        : this(options, TerminalGuiDispatcher.Instance) { }

    internal AgentsPaneView(TerminalGuiOptions options, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(options);
        _dispatcher = dispatcher;
        _glyphs = GlyphSet.Resolve(options.Glyphs, OutputIsUtf8());
        Width = Dim.Fill();
        Height = 0;
        SetScheme(SchemeFactory.Pane());

        _text = new MouseClipboardTextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            Multiline = true,
            WordWrap = false,
            // NOT focusable: the pane is informational, and a focusable read-only view
            // that appears mid-session is a focus thief — typed keys would land in it
            // and silently vanish. Mouse selection still works (MouseClipboardTextView
            // hooks OnMouseEvent regardless of focus, same as the history pane).
            CanFocus = false,
            ScrollBars = false,
        };
        _text.SetScheme(SchemeFactory.Dim());
        Add(_text);
    }

    /// <summary>Rows the pane wants right now (0 collapses it). The toplevel lays out from this.</summary>
    public int DesiredRows => _desiredRows;

    /// <summary>Raised when <see cref="DesiredRows"/> changed — the toplevel relayouts on it.</summary>
    public event EventHandler? RowsChanged;

    /// <summary>Wires the row source and starts the 1 Hz poll.</summary>
    public void Bind(TuiIntegration integration)
    {
        ArgumentNullException.ThrowIfNull(integration);
        _integration = integration;
        StartTimer();
        Refresh();
    }

    /// <summary>Re-polls the rows and repaints. Also used by tests.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Host-supplied delegate fault barrier: a throwing row provider collapses the pane, never crashes the UI timer.")]
    public void Refresh()
    {
        IReadOnlyList<AgentRowInfo> rows;
        try
        {
            rows = _integration?.AgentRows?.Invoke() ?? [];
        }
        catch
        {
            rows = [];
        }

        var visible = rows.Take(MaxRows).ToList();
        var width = Math.Max(20, Viewport.Width > 0 ? Viewport.Width : 80);
        var nameColumn = AgentsPaneModel.NameColumn(visible);
        var text = string.Join("\n", visible.Select(r => AgentsPaneModel.FormatRow(_glyphs, r, width, nameColumn)));

        var desired = visible.Count;
        _dispatcher.Invoke(() =>
        {
            _text.Text = text;
            if (desired != _desiredRows)
            {
                _desiredRows = desired;
                RowsChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void StartTimer()
    {
        if (_timerToken is not null) return;
        try
        {
            _timerToken = Application.AddTimeout(TimeSpan.FromSeconds(1), () =>
            {
                Refresh();
                return true;
            });
        }
        catch (InvalidOperationException)
        {
            // Application not initialised (unit tests) — Refresh() stays callable manually.
        }
    }

    private static bool OutputIsUtf8()
    {
        try
        {
            return System.Console.OutputEncoding.CodePage is 65001;
        }
        catch (System.IO.IOException)
        {
            return false;
        }
    }

    /// <summary>Test-only accessor for the rendered rows.</summary>
    internal string CurrentText => _text.Text.Replace("\r\n", "\n", StringComparison.Ordinal);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_timerToken is not null)
            {
                try { Application.RemoveTimeout(_timerToken); }
                catch (InvalidOperationException) { /* Application already shut down */ }
                _timerToken = null;
            }
            _text.Dispose();
        }
        base.Dispose(disposing);
    }
}
