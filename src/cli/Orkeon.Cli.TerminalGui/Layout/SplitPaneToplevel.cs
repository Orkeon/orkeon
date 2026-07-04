using Orkeon.Cli.TerminalGui.Hosting;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Top-level Window hosting three vertically stacked panes (top → bottom):
/// REPL (interactive prompt + history), Tasks (running command bandeau), Logs.
/// A StatusBar may be attached at the bottom edge.
/// </summary>
/// <remarks>
/// Pane geometry is recomputed in absolute rows on every layout pass (via the
/// <see cref="View.SubViewLayout"/> event) so it stays correct as the terminal
/// resizes, the status bar attaches/detaches, or panes toggle visibility.
/// At least one of REPL / Logs must remain visible.
/// </remarks>
public sealed class SplitPaneToplevel : Window
{
    private const int TasksPaneHeight = 3; // 1 row of content + 2 frame borders
    private const int MinPaneRows = 5;     // floor so a pane is never crushed below readability

    private double _splitRatio;
    private StatusBar? _statusBar;
    private bool _logsVisible = true;
    private bool _replVisible = true;
    private bool _tasksVisible = true;

    public Orkeon.Cli.Abstractions.Runners.IInteractiveRunner? Runner { get; set; }

    public SplitPaneToplevel(TerminalGuiOptions options)
        : this(options, TerminalGuiDispatcher.Instance) { }

    internal SplitPaneToplevel(TerminalGuiOptions options, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(options);
        _splitRatio = ClampRatio(options.InitialSplitRatio);
        SetScheme(SchemeFactory.Pane());

        Repl = new ReplPaneView(options, dispatcher) { X = 0, Y = 0, Width = Dim.Fill() };
        Tasks = new TasksPaneView(options, dispatcher, TimeProvider.System) { X = 0, Y = 0, Width = Dim.Fill() };
        Logs = new LogsPaneView(options, dispatcher) { X = 0, Y = 0, Width = Dim.Fill() };
        Add(Repl, Tasks, Logs);

        // Right-click anywhere in the TUI pastes the OS clipboard into the
        // REPL input — the only editable surface in the split-pane. The
        // subscription is kept alive for the lifetime of this toplevel by the
        // static Application.MouseEvent itself (no Dispose needed; it unsubscribes
        // implicitly on shutdown), so the returned handle is intentionally discarded.
        AttachRightClickPaste();

        // Recompute absolute pane heights every layout pass so the geometry tracks
        // terminal resizes and visibility toggles without relying on Dim arithmetic
        // (Dim.Fill(Dim.Percent(...) + ...) does not reliably resolve in v2.1.0).
        SubViewLayout += (_, _) => ApplyLayout();
        ApplyLayout();
    }

    public ReplPaneView Repl { get; }

    public TasksPaneView Tasks { get; }

    public LogsPaneView Logs { get; }

    public double CurrentSplitRatio => _splitRatio;

    public void SetSplitRatio(double ratio)
    {
        _splitRatio = ClampRatio(ratio);
        ApplyLayout();
    }

    public bool IsLogsVisible => _logsVisible;

    public bool IsReplVisible => _replVisible;

    public bool IsTasksVisible => _tasksVisible;

    public void ToggleLogsVisible()
    {
        if (_logsVisible && !_replVisible) return;
        _logsVisible = !_logsVisible;
        ApplyLayout();
    }

    public void ToggleReplVisible()
    {
        if (_replVisible && !_logsVisible) return;
        _replVisible = !_replVisible;
        ApplyLayout();
    }

    public void ToggleTasksVisible()
    {
        _tasksVisible = !_tasksVisible;
        ApplyLayout();
    }

    public void AttachStatusBar(StatusBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        if (_statusBar is not null)
            throw new InvalidOperationException("A StatusBar is already attached to this toplevel.");
        bar.X = 0;
        bar.Y = Pos.AnchorEnd(1);
        bar.Width = Dim.Fill();
        bar.Height = 1;
        _statusBar = bar;
        Add(bar);
        ApplyLayout();
    }

    /// <summary>
    /// Recomputes pane geometry in absolute rows, honoring visibility flags +
    /// split ratio + current toplevel content height.
    /// </summary>
    private void ApplyLayout()
    {
        Repl.Visible = _replVisible;
        Tasks.Visible = _tasksVisible;
        Logs.Visible = _logsVisible;

        // Total rows of usable content area inside the toplevel's borders.
        var totalRows = GetContentSize().Height;
        if (totalRows <= 0) totalRows = 24; // fallback before first layout

        var statusRows = _statusBar is null ? 0 : 1;
        var tasksRows  = _tasksVisible ? Math.Min(TasksPaneHeight, totalRows - statusRows) : 0;
        var available  = Math.Max(0, totalRows - statusRows - tasksRows);

        int logsRows;
        int replRows;
        if (_logsVisible && _replVisible)
        {
            logsRows = Math.Clamp((int)Math.Round(_splitRatio * available), MinPaneRows, Math.Max(MinPaneRows, available - MinPaneRows));
            replRows = available - logsRows;
        }
        else if (_logsVisible)
        {
            logsRows = available;
            replRows = 0;
        }
        else
        {
            // Either only the REPL is visible, or (defensively — toggles refuse to hide
            // both) neither is: give all available rows to the REPL.
            logsRows = 0;
            replRows = available;
        }

        Repl.X = 0;
        Repl.Y = 0;
        Repl.Width = Dim.Fill();
        Repl.Height = replRows;

        Tasks.X = 0;
        Tasks.Width = Dim.Fill();
        Tasks.Y = replRows;
        Tasks.Height = tasksRows;

        Logs.X = 0;
        Logs.Width = Dim.Fill();
        Logs.Y = replRows + tasksRows;
        Logs.Height = logsRows;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "The returned IDisposable only unsubscribes the static Application.MouseEvent handler; its lifetime is intentionally bound to that static event for the toplevel's entire lifetime (production never unsubscribes — only tests do). It owns no unmanaged or scarce resource, so deterministic disposal in this scope is neither needed nor correct.")]
    private void AttachRightClickPaste()
        => _ = MouseClipboardBehavior.AttachRightClickPaste(() => Repl.Input);

    private static double ClampRatio(double ratio)
        => Math.Clamp(ratio, 0.1, 0.9);
}
