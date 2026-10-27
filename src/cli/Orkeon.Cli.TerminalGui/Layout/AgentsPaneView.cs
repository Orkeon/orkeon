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
/// The tasks list under the status bar (PLAN C9): <c>main</c> first, then one row per
/// in-flight async command instance in creation order. Rows come from the host through
/// <see cref="TuiIntegration.AgentRows"/> and are re-polled at 1 Hz — the command
/// registry emits no change events, so polling is the mechanism, same as the
/// pre-fidelity bandeau.
/// </summary>
/// <remarks>
/// Selection: the pane stays NON-focusable at rest (the first live launch proved a
/// focusable read-only pane is a focus thief — typed keys landed in it and vanished).
/// ↓ from the prompt's last row, or F4, opts in explicitly: <see cref="EnterSelectionMode"/>
/// makes the pane focusable, arrows move the cursor, Enter raises <see cref="RowActivated"/>
/// with the row's ticket, ↑ on the first row, Esc (or the pane emptying) leaves and raises
/// <see cref="SelectionExited"/> so the host can hand focus back to the prompt. Once the REPL
/// pane is attached (<see cref="ReplPaneView.AttachTaskList"/>) the keys are read by its
/// navigation machine, and the selection is kept by ticket so the poll's re-sort never moves
/// it. A mouse click enters the list on its row through that machine (the list takes the focus,
/// so the <c>❯</c> cursor is never drawn while the keys go to the prompt); a double-click also
/// activates the row.
/// Rendering: each row is drawn segment by segment (<see cref="AgentsPaneModel.Segments"/>)
/// through the text view's per-cell attribute, so drag-select and copy keep working. The
/// list grows with its rows up to <see cref="MaxRows"/>, then scrolls with the cursor, and the
/// rows are rebuilt at the pane's width whenever its viewport changes. A ticket that appears or
/// leaves between two polls, or that ended in between (<see cref="TuiIntegration.FinishedAgentRows"/>),
/// is written to the transcript (<see cref="TaskTranscriptTracker"/>).
/// </remarks>
public sealed class AgentsPaneView : View
{
    /// <summary>Rows shown before the list scrolls, so a burst of tickets cannot swallow the transcript.</summary>
    internal const int MaxRows = 6;

    private readonly MouseClipboardTextView _text;
    private readonly IUiDispatcher _dispatcher;
    private readonly GlyphSet _glyphs;
    private TuiIntegration? _integration;
    private object? _timerToken;
    private int _desiredRows;
    private List<AgentRowInfo> _rows = [];
    private bool _selectionActive;
    private string? _selectedTicket; // null names the `main` row
    private int _lastIndex;
    private int _top;
    private List<IReadOnlyList<RowSegment>> _painted = [];
    private int _paintedWidth;
    private readonly TaskTranscriptTracker _tracker;
    private System.Drawing.Point? _pressedAt;
    private bool _dragged;

    public AgentsPaneView(TerminalGuiOptions options)
        : this(options, TerminalGuiDispatcher.Instance) { }

    internal AgentsPaneView(TerminalGuiOptions options, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(options);
        _dispatcher = dispatcher;
        _glyphs = GlyphSet.Resolve(options.Glyphs, Hosting.TerminalUtf8.IsUtf8(options.Terminal));
        _tracker = new TaskTranscriptTracker(_glyphs);
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
        _text.SetScheme(SchemeFactory.Pane());
        _text.CellAttribute = AttributeAt;
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
    public bool SelectionActive => _selectionActive;

    /// <summary>The prompt's navigation machine, once the REPL pane attached itself.</summary>
    internal ITaskListNavigator? Navigator { get; set; }

    /// <summary>The rows of the list, in display order (the window shows up to <see cref="MaxRows"/> of them).</summary>
    internal IReadOnlyList<AgentRowInfo> Rows => _rows;

    /// <summary>Receives the transcript lines of tasks launched or finished between two polls.</summary>
    internal Action<string>? TranscriptSink { get; set; }

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
        if (Navigator is { } navigator)
        {
            navigator.EnterList();
            return;
        }
        if (_rows.Count == 0) return;
        ShowSelection(Ticket(_rows[0]));
    }

    /// <summary>Leaves selection mode and tells the host to restore the prompt focus.</summary>
    public void ExitSelectionMode()
    {
        if (Navigator is { } navigator && SelectionActive)
        {
            navigator.LeaveList();
            return;
        }
        HideSelection();
    }

    /// <summary>Takes the focus with the cursor on the row of <paramref name="ticket"/> (null: <c>main</c>).</summary>
    internal void ShowSelection(string? ticket)
    {
        if (_rows.Count == 0) return;
        _selectionActive = true;
        _selectedTicket = ticket;
        CanFocus = true;
        SetFocus();
        Repaint();
    }

    /// <summary>Moves the cursor to the row of <paramref name="ticket"/> (null: <c>main</c>).</summary>
    internal void SelectTicket(string? ticket)
    {
        _selectedTicket = ticket;
        Repaint();
    }

    /// <summary>Drops the cursor and the focus, and tells the host to restore the prompt focus.</summary>
    internal void HideSelection()
    {
        if (!SelectionActive && !CanFocus) return;
        _selectionActive = false;
        _selectedTicket = null;
        CanFocus = false;
        Repaint();
        SelectionExited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raises <see cref="RowActivated"/> for <paramref name="ticket"/>: the task's view.</summary>
    internal void ActivateTicket(string ticket)
        => RowActivated?.Invoke(this, new AgentRowActivatedEventArgs(ticket));

    /// <inheritdoc />
    protected override bool OnKeyDown(Key key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!SelectionActive) return base.OnKeyDown(key);
        if (Navigator is { } navigator)
            return navigator.HandleListKey(key) || base.OnKeyDown(key);

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
        var index = Math.Clamp(Math.Max(0, SelectedIndex) + delta, 0, _rows.Count - 1);
        SelectTicket(Ticket(_rows[index]));
    }

    private void ActivateSelected()
    {
        var index = SelectedIndex;
        if (index < 0 || index >= _rows.Count) return;
        // `main` carries no ticket — there is nothing to inspect on the REPL itself.
        if (_rows[index].Ticket is { Length: > 0 } ticket)
            ActivateTicket(ticket);
    }

    private static string? Ticket(AgentRowInfo row) => string.IsNullOrEmpty(row.Ticket) ? null : row.Ticket;

    private int IndexOfTicket(string? ticket)
        => _rows.FindIndex(r => string.Equals(Ticket(r), ticket, StringComparison.Ordinal));

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

    private void OnGlobalMouseEvent(object? sender, Mouse e) => HandleMouse(e);

    /// <summary>One application-wide mouse event, screen-relative. Also used by tests.</summary>
    internal void HandleMouse(Mouse e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (TrackDrag(e)) return;
        var single = e.Flags.HasFlag(MouseFlags.LeftButtonClicked);
        var doubleClick = e.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked);
        if (!single && !doubleClick) return;
        // Terminal.Gui raises the click on release: a press elsewhere, or a drag between the
        // two, is a drag-select (copy), not a click on the row it ends on.
        if (single && (_dragged || (_pressedAt is { } pressedAt && pressedAt != e.ScreenPosition))) return;
        if (!Visible || _rows.Count == 0) return;

        var frame = FrameToScreen();
        if (!frame.Contains(e.ScreenPosition)) return;

        if (ClickRow(e.ScreenPosition.Y - frame.Y, doubleClick)) e.Handled = doubleClick;
    }

    /// <summary>
    /// Records where the left button went down and whether it moved before its release.
    /// </summary>
    /// <returns>True when <paramref name="e"/> is a press or a drag report, which nothing else reads.</returns>
    private bool TrackDrag(Mouse e)
    {
        if (e.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            if (!e.Flags.HasFlag(MouseFlags.PositionReport))
            {
                _pressedAt = e.ScreenPosition;
                _dragged = false;
            }
            else if (_pressedAt is { } pressedAt && pressedAt != e.ScreenPosition)
            {
                _dragged = true;
            }
            return true;
        }
        if (e.Flags.HasFlag(MouseFlags.LeftButtonReleased) && _pressedAt is { } at && at != e.ScreenPosition)
            _dragged = true;
        return false;
    }

    /// <summary>
    /// A click on the <paramref name="visibleRow"/>-th row of the window: the list takes the focus
    /// on that row — through the navigation machine once attached, so its state, the recipient and
    /// the hint bar follow — and a double-click then opens the row's task.
    /// </summary>
    /// <returns>True when the click landed on a row.</returns>
    internal bool ClickRow(int visibleRow, bool doubleClick)
    {
        var row = _top + visibleRow;
        if (row < 0 || row >= _rows.Count) return false;

        var ticket = Ticket(_rows[row]);
        if (Navigator is { } navigator) navigator.SelectRow(ticket);
        else if (SelectionActive) SelectTicket(ticket);
        else ShowSelection(ticket);
        if (doubleClick) ActivateSelected();
        return true;
    }

    private IReadOnlyList<AgentRowInfo> _lastFinished = [];

    /// <summary>Re-polls the rows and repaints. Also used by tests.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Host-supplied delegate fault barrier: a throwing row provider leaves the pane as it was, never crashes the UI timer.")]
    public void Refresh()
    {
        IReadOnlyList<AgentRowInfo> rows;
        try
        {
            rows = _integration?.AgentRows?.Invoke() ?? [];
        }
        catch
        {
            // A faulted poll says nothing about the tasks: the rows, the tracker, the selection
            // and the recipient keep what they knew, so it writes no line and moves no focus,
            // and the next good poll reports neither a false end nor a false launch.
            return;
        }

        var hasFeed = TryReadFinished(out var finished);
        if (hasFeed) _lastFinished = finished;
        _rows = AgentsPaneModel.ComposeListRows(rows, hasFeed ? finished : _lastFinished).ToList();
        if (TranscriptSink is { } sink)
        {
            foreach (var line in _tracker.Observe(rows, hasFeed ? finished : null)) sink(line);
        }
        if (Navigator is { } navigator)
        {
            // The machine keeps the selection by ticket and decides what an emptied list does,
            // and the repaint comes after it, so the recipient's ● follows a recipient that changed.
            navigator.RowsRefreshed(_rows);
            Repaint();
            return;
        }
        if (_rows.Count == 0 && (SelectionActive || CanFocus))
        {
            HideSelection();
            return;
        }
        if (SelectionActive && IndexOfTicket(_selectedTicket) < 0)
            _selectedTicket = Ticket(_rows[Math.Min(_lastIndex, _rows.Count - 1)]); // the row aged out under the cursor
        Repaint();
    }

    /// <summary>The ended tasks the host reports, read after the rows; false when not wired or faulted.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Host-supplied delegate fault barrier: a throwing feed degrades to the rows-only transcript lines.")]
    private bool TryReadFinished(out IReadOnlyList<AgentRowInfo> finished)
    {
        finished = [];
        if (_integration?.FinishedAgentRows is not { } feed)
            return false;

        try
        {
            finished = feed();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    protected override void OnViewportChanged(DrawEventArgs e)
    {
        base.OnViewportChanged(e);
        // The rows are cut and padded to the width: a resize rebuilds them at the new one.
        if (_integration is not null && Viewport.Width > 0 && Viewport.Width != _paintedWidth) Repaint();
    }

    /// <summary>Renders the window of <see cref="_rows"/> into the text view and relayouts on row-count change.</summary>
    private void Repaint()
    {
        var selected = SelectedIndex;
        if (selected >= 0) _lastIndex = selected;
        var visibleCount = Math.Min(_rows.Count, MaxRows);
        _top = AgentsPaneModel.WindowTop(_rows.Count, visibleCount, selected, _top);
        // Before the first layout the width is unknown: 80 columns, rebuilt once the viewport is set.
        var width = Viewport.Width > 0 ? Viewport.Width : 80;
        _paintedWidth = Viewport.Width;
        var recipient = Navigator?.RecipientTicket;
        var painted = new List<IReadOnlyList<RowSegment>>(visibleCount);
        for (var i = _top; i < _top + visibleCount; i++)
        {
            var row = _rows[i];
            painted.Add(AgentsPaneModel.Segments(
                _glyphs, row, width,
                cursor: i == selected,
                recipient: string.Equals(Ticket(row), recipient, StringComparison.Ordinal)));
        }
        var text = string.Join("\n", painted.Select(RowSegment.Concat));

        var desired = visibleCount;
        _dispatcher.Invoke(() =>
        {
            _painted = painted;
            _text.Text = text;
            if (desired != _desiredRows)
            {
                _desiredRows = desired;
                RowsChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    /// <summary>The attribute of one cell (one grapheme) of the window: its segment's role, or null past the row.</summary>
    private Terminal.Gui.Drawing.Attribute? AttributeAt(int row, int cell)
    {
        var painted = _painted;
        if (row < 0 || row >= painted.Count) return null;
        return AgentsPaneModel.SegmentAtCell(painted[row], cell) is { } segment
            ? SchemeFactory.Attr(segment.Role, segment.Bold, segment.Italic)
            : null;
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

    /// <summary>Test-only accessor for the rendered rows.</summary>
    internal string CurrentText => _text.Text.Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Test-only accessor for the selection cursor.</summary>
    internal int SelectedIndex => _selectionActive ? IndexOfTicket(_selectedTicket) : -1;

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
