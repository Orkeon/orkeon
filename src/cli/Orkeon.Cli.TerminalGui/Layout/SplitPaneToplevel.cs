using Orkeon.Cli.TerminalGui.Hosting;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Top-level Window of the fidelity layout (PLAN phase 1). Top → bottom:
/// the logs DRAWER (hidden by default — our deliberate addition, framed, opens at the
/// top so the interactive cluster below never moves), the REPL (borderless transcript
/// + status line + rule/chip + prompt, all inside <see cref="ReplPaneView"/>), the
/// one-row hint bar, and the agents rows.
/// </summary>
/// <remarks>
/// Pane geometry is recomputed in absolute rows on every layout pass (via the
/// <see cref="View.SubViewLayout"/> event) so it stays correct as the terminal
/// resizes, panes toggle, or the agents list grows. At least one of REPL / Logs
/// stays visible.
/// </remarks>
public sealed class SplitPaneToplevel : Window
{
    private const int MinPaneRows = 5; // floor so a pane is never crushed below readability

    private double _splitRatio;
    private bool _logsVisible;
    private bool _replVisible = true;

    public Orkeon.Cli.Abstractions.Runners.IInteractiveRunner? Runner { get; set; }

    public SplitPaneToplevel(TerminalGuiOptions options)
        : this(options, TerminalGuiDispatcher.Instance) { }

    internal SplitPaneToplevel(TerminalGuiOptions options, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(options);
        _splitRatio = ClampRatio(options.InitialSplitRatio);
        _logsVisible = options.LogsVisibleAtStartup;
        SetScheme(SchemeFactory.Pane());
        // The toplevel itself is chromeless: the reference UI has no outer frame.
        BorderStyle = Terminal.Gui.Drawing.LineStyle.None;

        Repl = new ReplPaneView(options, dispatcher) { X = 0, Y = 0, Width = Dim.Fill() };
        Logs = new LogsPaneView(options, dispatcher) { X = 0, Y = 0, Width = Dim.Fill() };
        HintBar = new HintBarView(options, dispatcher) { X = 0, Width = Dim.Fill() };
        Agents = new AgentsPaneView(options, dispatcher) { X = 0, Width = Dim.Fill() };
        Add(Logs, Repl, HintBar, Agents);

        Agents.RowsChanged += (_, _) => ApplyLayout();

        // Right-click anywhere in the TUI pastes the OS clipboard into the
        // REPL input — the only editable surface in the layout. The subscription is
        // kept alive for the lifetime of this toplevel by the static
        // Application.MouseEvent itself (no Dispose needed; it unsubscribes
        // implicitly on shutdown), so the returned handle is intentionally discarded.
        AttachRightClickPaste();

        // Recompute absolute pane heights every layout pass so the geometry tracks
        // terminal resizes and visibility toggles without relying on Dim arithmetic
        // (composed Dim expressions did not reliably resolve when this was written
        // against 2.1.0; the absolute recompute also absorbs any 2.4.x drift).
        SubViewLayout += (_, _) => ApplyLayout();
        ApplyLayout();
    }

    public ReplPaneView Repl { get; }

    public LogsPaneView Logs { get; }

    public HintBarView HintBar { get; }

    public AgentsPaneView Agents { get; }

    public double CurrentSplitRatio => _splitRatio;

    public void SetSplitRatio(double ratio)
    {
        _splitRatio = ClampRatio(ratio);
        ApplyLayout();
    }

    public bool IsLogsVisible => _logsVisible;

    public bool IsReplVisible => _replVisible;

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

    /// <summary>
    /// Recomputes pane geometry in absolute rows, honoring visibility flags +
    /// drawer ratio + the agents list's current row count.
    /// </summary>
    private void ApplyLayout()
    {
        Repl.Visible = _replVisible;
        Logs.Visible = _logsVisible;

        var totalRows = GetContentSize().Height;
        if (totalRows <= 0) totalRows = 24; // fallback before first layout

        const int hintRows = 1;
        var agentsRows = Math.Min(Agents.DesiredRows, AgentsPaneView.MaxRows);
        var available = Math.Max(0, totalRows - hintRows - agentsRows);

        int logsRows;
        int replRows;
        if (_logsVisible && _replVisible)
        {
            logsRows = Math.Clamp(
                (int)Math.Round(_splitRatio * available),
                MinPaneRows,
                Math.Max(MinPaneRows, available - MinPaneRows));
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

        // Drawer on TOP: the interactive cluster (prompt, hint bar, agents) keeps its
        // place at the bottom whether the drawer is open or not.
        Logs.X = 0;
        Logs.Y = 0;
        Logs.Width = Dim.Fill();
        Logs.Height = logsRows;

        Repl.X = 0;
        Repl.Y = logsRows;
        Repl.Width = Dim.Fill();
        Repl.Height = replRows;

        HintBar.X = 0;
        HintBar.Y = logsRows + replRows;
        HintBar.Width = Dim.Fill();
        HintBar.Height = hintRows;

        Agents.X = 0;
        Agents.Y = logsRows + replRows + hintRows;
        Agents.Width = Dim.Fill();
        Agents.Height = agentsRows;
        Agents.Visible = agentsRows > 0;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "The returned IDisposable only unsubscribes the static Application.MouseEvent handler; its lifetime is intentionally bound to that static event for the toplevel's entire lifetime (production never unsubscribes — only tests do). It owns no unmanaged or scarce resource, so deterministic disposal in this scope is neither needed nor correct.")]
    private void AttachRightClickPaste()
        => _ = MouseClipboardBehavior.AttachRightClickPaste(() => Repl.Input);

    private static double ClampRatio(double ratio)
        => Math.Clamp(ratio, 0.1, 0.9);
}
