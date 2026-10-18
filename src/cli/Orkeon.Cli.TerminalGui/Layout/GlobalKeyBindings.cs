using Microsoft.Extensions.Logging;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Logging;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Application-wide key bindings for the fidelity layout, replacing the Terminal.Gui
/// StatusBar (PLAN phase 5): the visible bar is now <see cref="HintBarView"/>, and every
/// shortcut lives here as a global <see cref="Application.KeyDown"/> handler — the same
/// mechanism the old builder already used for its secondary keys, which is why the
/// bindings survive the widget's removal unchanged.
/// </summary>
/// <remarks>
/// Ctrl+T moved to Ctrl+G (toggle loGs): Ctrl+T used to toggle the now-removed tasks
/// bandeau, and reusing it for logs would retrain muscle memory onto a different effect.
/// Esc-to-interrupt has no global binding: the REPL input's Esc-Esc pair raises
/// <c>InterruptRequested</c>, and Application.KeyDown fires before view dispatch, so a
/// global Esc would steal it from the focused panes.
/// </remarks>
public static class GlobalKeyBindings
{
    /// <summary>
    /// Verbosity ladder, ordered from MOST verbose (index 0 = Trace) to LEAST verbose
    /// (index N-1 = Error). F2 moves the index DOWN (more details). Shift+F2 up (fewer).
    /// </summary>
    private static readonly LogLevel[] VerbosityLadder =
    {
        LogLevel.Trace, LogLevel.Debug, LogLevel.Information,
        LogLevel.Warning, LogLevel.Error,
    };

    /// <summary>Installs every global binding. Call once per toplevel lifetime.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "UI-shortcut fault barrier: a failing shortcut action is swallowed so it can never crash the Terminal.Gui key-dispatch loop.")]
    public static void Install(
        SplitPaneToplevel toplevel,
        TerminalGuiLoggerProvider logProvider,
        FindDialog findDialog,
        TuiIntegration? integration = null)
    {
        ArgumentNullException.ThrowIfNull(toplevel);
        ArgumentNullException.ThrowIfNull(logProvider);
        ArgumentNullException.ThrowIfNull(findDialog);

        toplevel.Logs.SetVisibleLevel(logProvider.CurrentMinimumLevel);

        void ApplyLevel(LogLevel next)
        {
            logProvider.SetMinimumLevel(next);
            toplevel.Logs.SetVisibleLevel(next);
        }

        var bindings = new (Func<Key, bool> Match, Action Action)[]
        {
            (k => IsCtrl(k, KeyCode.F), findDialog.Show),
            (k => IsCtrl(k, KeyCode.Q), () => HandleQuit(toplevel, integration)),
            (k => IsCtrl(k, KeyCode.G), toplevel.ToggleLogsVisible),
            (k => IsCtrl(k, KeyCode.R), toplevel.ToggleReplVisible),
            (k => IsCtrl(k, KeyCode.L), toplevel.Logs.Clear),
            (k => IsCtrl(k, KeyCode.K), toplevel.Repl.Clear),
            (k => k.KeyCode == Key.F3.KeyCode, toplevel.Repl.ToggleWrap),
            (k => IsCtrl(k, KeyCode.CursorUp), () => toplevel.SetSplitRatio(toplevel.CurrentSplitRatio + 0.05)),
            (k => IsCtrl(k, KeyCode.CursorDown), () => toplevel.SetSplitRatio(toplevel.CurrentSplitRatio - 0.05)),
            (k => k.KeyCode == Key.F2.KeyCode && !k.IsShift, () => ApplyLevel(MoreVerbose(logProvider.CurrentMinimumLevel))),
            (k => k.KeyCode == Key.F2.WithShift.KeyCode || (k.KeyCode == Key.F2.KeyCode && k.IsShift), () => ApplyLevel(LessVerbose(logProvider.CurrentMinimumLevel))),
            // Agents-pane selection. F4, not Ctrl+A: the panes' select-all already owns
            // Ctrl+A when focused, and a global handler fires before view dispatch.
            (k => k.KeyCode == Key.F4.KeyCode, () => ToggleAgentSelection(toplevel)),
            // Fidelity bindings (hint bar contract):
            (k => k.KeyCode == Key.Tab.WithShift.KeyCode, () => CycleMode(toplevel, integration)),
        };

        // Selection plumbing: Enter/double-click prints the instance detail into the
        // transcript; leaving selection hands focus back to the prompt — the exact
        // focus-thief scenario the pane's at-rest non-focusability guards against.
        toplevel.Agents.RowActivated += (_, e) =>
        {
            var detail = SafeDescribe(integration, e.Ticket);
            toplevel.Repl.AppendOutputLine(detail ?? $"[{e.Ticket}] no detail available.");
        };
        toplevel.Repl.InterruptRequested += (_, _) => OnInterruptRequested(toplevel, integration);
        toplevel.Agents.SelectionExited += (_, _) => toplevel.Repl.Input?.SetFocus();

        Application.KeyDown += (_, key) =>
        {
            if (key.Handled) return;
            foreach (var (match, action) in bindings)
            {
                if (!match(key)) continue;
                try { action(); }
                catch { /* swallow — a UI shortcut must never crash the loop */ }
                key.Handled = true;
                return;
            }
        };
    }

    private static bool IsCtrl(Key key, KeyCode letter)
        => key.IsCtrl && (key.KeyCode & ~(KeyCode.CtrlMask | KeyCode.AltMask | KeyCode.ShiftMask)) == letter;

    private static void CycleMode(SplitPaneToplevel toplevel, TuiIntegration? integration)
    {
        integration?.CyclePermissionMode?.Invoke();
        toplevel.HintBar.Refresh();
    }

    private static void ToggleAgentSelection(SplitPaneToplevel toplevel)
    {
        if (toplevel.Agents.SelectionActive) toplevel.Agents.ExitSelectionMode();
        else toplevel.Agents.EnterSelectionMode();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Host-supplied delegate fault barrier: a throwing describer degrades to the no-detail line, never crashes key dispatch.")]
    private static string? SafeDescribe(TuiIntegration? integration, string ticket)
    {
        try
        {
            return integration?.DescribeAgent?.Invoke(ticket);
        }
        catch
        {
            return null;
        }
    }

    private static void Interrupt(SplitPaneToplevel toplevel, TuiIntegration? integration)
    {
        // A running command is cancelled at the runner (Ctrl+C path); the prompt's turn
        // is interrupted by the host's targeted cancel. Both run: both are cancelled.
        if (toplevel.Runner?.IsCommandRunning == true) toplevel.Runner.RequestCommandCancellation();
        if (integration?.PromptTurnRunning?.Invoke() == true) integration.InterruptCurrent?.Invoke();
        toplevel.Repl.AppendOutputLine("⏹  interrupt requested (esc).");
    }

    private static void HandleQuit(SplitPaneToplevel toplevel, TuiIntegration? integration)
    {
        if (QuitNeedsConfirmation(toplevel.Runner, integration))
        {
            var choice = MessageBox.Query(
                Application.Instance!,
                "Quit?",
                "A command is currently running. Do you want to terminate the application?",
                "Yes — terminate now", "No — keep running");
            if (choice != 0) return;
        }
        Application.RequestStop(toplevel);
    }

    internal static void OnInterruptRequested(SplitPaneToplevel toplevel, TuiIntegration? integration)
    {
        if (HintBarView.IsTurnRunning(toplevel.Runner, integration))
            Interrupt(toplevel, integration);
    }

    internal static bool QuitNeedsConfirmation(Orkeon.Cli.Abstractions.Runners.IInteractiveRunner? runner, TuiIntegration? integration)
        => HintBarView.IsTurnRunning(runner, integration);

    internal static LogLevel MoreVerbose(LogLevel current)
    {
        var idx = Array.IndexOf(VerbosityLadder, current);
        if (idx <= 0) return VerbosityLadder[0];
        return VerbosityLadder[idx - 1];
    }

    internal static LogLevel LessVerbose(LogLevel current)
    {
        var idx = Array.IndexOf(VerbosityLadder, current);
        if (idx < 0) return LogLevel.Information;
        if (idx >= VerbosityLadder.Length - 1) return VerbosityLadder[^1];
        return VerbosityLadder[idx + 1];
    }

    internal static string FormatLevelLabel(LogLevel level) => $"Level: {level}";
}
