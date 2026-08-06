using Orkeon.Cli.TerminalGui.Hosting;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>Payload of <see cref="AgentsPaneView.RowActivated"/>: the activated row's ticket.</summary>
public sealed class AgentRowActivatedEventArgs : EventArgs
{
    public AgentRowActivatedEventArgs(string ticket) => Ticket = ticket;

    /// <summary>Instance ticket of the activated row.</summary>
    public string Ticket { get; }
}

/// <summary>
/// The agents list under the hint bar (PLAN C9): one row per in-flight or recent
/// async command instance, plus <c>main</c>. Rows come from the host through
/// <see cref="TuiIntegration.AgentRows"/> and are re-polled at 1 Hz — the command
/// registry emits no change events, so polling is the mechanism, same as the
/// pre-fidelity bandeau.
/// </summary>
/// <remarks>
/// Selection: the pane stays NON-focusable at rest (the first live launch proved a
/// focusable read-only pane is a focus thief — typed keys landed in it and vanished).
/// F4 opts in explicitly: <see cref="EnterSelectionMode"/> makes the pane focusable,
/// arrows move the cursor, Enter raises <see cref="RowActivated"/> with the row's
/// ticket, Esc (or the pane collapsing) leaves and raises <see cref="SelectionExited"/>
/// so the host can hand focus back to the prompt. A mouse click selects a row without
/// stealing focus; a double-click activates it.
/// </remarks>
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
    private List<AgentRowInfo> _rows = [];
    private int _selected = -1;

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
            // and silently vanish. Selection focus lives on the CONTAINER, entered only
            // through F4 (EnterSelectionMode) and left on Esc. Mouse selection still
            // works (MouseClipboardTextView hooks OnMouseEvent regardless of focus,
            // same as the history pane).
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

    /// <summary>Raised on Enter / double-click with the selected row's ticket.</summary>
    public event EventHandler<AgentRowActivatedEventArgs>? RowActivated;

    /// <summary>Raised when selection mode ends — the host hands focus back to the prompt.</summary>
    public event EventHandler? SelectionExited;

    /// <summary>True while the pane owns the selection cursor (F4 mode).</summary>
    public bool SelectionActive => _selected >= 0;

    /// <summary>Wires the row source and starts the 1 Hz poll.</summary>
    public void Bind(TuiIntegration integration)
    {
        ArgumentNullException.ThrowIfNull(integration);
        _integration = integration;
        StartTimer();
        AttachMouseSelection();
        Refresh();
    }

    /// <summary>
    /// Enters selection mode (F4): the pane becomes focusable, takes focus, and the
    /// cursor lands on the first row. No-op while the pane is empty.
    /// </summary>
    public void EnterSelectionMode()
    {
        if (_rows.Count == 0) return;
        _selected = 0;
        CanFocus = true;
        SetFocus();
        Repaint();
    }

    /// <summary>Leaves selection mode and tells the host to restore the prompt focus.</summary>
    public void ExitSelectionMode()
    {
        if (!SelectionActive && !CanFocus) return;
        _selected = -1;
        CanFocus = false;
        Repaint();
        SelectionExited?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override bool OnKeyDown(Key key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!SelectionActive) return base.OnKeyDown(key);

        switch (key.KeyCode)
        {
            case KeyCode.CursorUp:
                MoveSelection(-1);
                return true;
            case KeyCode.CursorDown:
                MoveSelection(+1);
                return true;
            case KeyCode.Enter:
                ActivateSelected();
                return true;
            case KeyCode.Esc:
                ExitSelectionMode();
                return true;
            default:
                return base.OnKeyDown(key);
        }
    }

    private void MoveSelection(int delta)
    {
        if (_rows.Count == 0) return;
        _selected = Math.Clamp(_selected + delta, 0, _rows.Count - 1);
        Repaint();
    }

    private void ActivateSelected()
    {
        if (_selected < 0 || _selected >= _rows.Count) return;
        // `main` carries no ticket — there is nothing to inspect on the REPL itself.
        if (_rows[_selected].Ticket is { Length: > 0 } ticket)
            RowActivated?.Invoke(this, new AgentRowActivatedEventArgs(ticket));
    }

    /// <summary>
    /// Mouse: a single click moves the selection cursor WITHOUT taking focus (typed keys
    /// must keep landing in the prompt); a double-click activates the row. Subscribed on
    /// <see cref="Application.MouseEvent"/> because the inner text view owns the direct
    /// mouse path for drag-select/copy.
    /// </summary>
    private void AttachMouseSelection()
    {
        Application.MouseEvent += OnGlobalMouseEvent;
    }

    private void OnGlobalMouseEvent(object? sender, Mouse e)
    {
        var single = e.Flags.HasFlag(MouseFlags.LeftButtonClicked);
        var doubleClick = e.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked);
        if (!single && !doubleClick) return;
        if (!Visible || _rows.Count == 0) return;

        var frame = FrameToScreen();
        if (!frame.Contains(e.ScreenPosition)) return;

        var row = e.ScreenPosition.Y - frame.Y;
        if (row < 0 || row >= _rows.Count) return;

        _selected = row;
        Repaint();
        if (doubleClick)
        {
            ActivateSelected();
            e.Handled = true;
        }
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

        _rows = rows.Take(MaxRows).ToList();
        if (_selected >= _rows.Count)
            _selected = _rows.Count - 1; // clamp: rows aged out under the cursor
        if (_rows.Count == 0 && (SelectionActive || CanFocus))
            ExitSelectionMode();
        Repaint();
    }

    /// <summary>Renders <see cref="_rows"/> into the text view and relayouts on row-count change.</summary>
    private void Repaint()
    {
        var visible = _rows;
        var width = Math.Max(20, Viewport.Width > 0 ? Viewport.Width : 80);
        var nameColumn = AgentsPaneModel.NameColumn(visible);
        var text = string.Join("\n", visible.Select(
            (r, i) => AgentsPaneModel.FormatRow(_glyphs, r, width, nameColumn, selected: i == _selected)));

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

    /// <summary>Test-only accessor for the selection cursor.</summary>
    internal int SelectedIndex => _selected;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.MouseEvent -= OnGlobalMouseEvent;
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
