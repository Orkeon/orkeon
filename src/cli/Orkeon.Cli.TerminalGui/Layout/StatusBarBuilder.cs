using Microsoft.Extensions.Logging;
using Orkeon.Cli.TerminalGui.Logging;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Factory that wires the StatusBar with a compact set of must-see shortcuts
/// (Find / Cancel / Quit / Toggle logs / More…) plus a "More…" popover dialog
/// that exposes the remaining commands. Secondary keystrokes (Ctrl+L/K/R/T,
/// Ctrl+↑/↓, F2, Shift+F2) are bound globally via <see cref="Application.KeyDown"/>
/// so they remain active even though the corresponding slots are hidden.
/// </summary>
public static class StatusBarBuilder
{
    /// <summary>
    /// Verbosity ladder, ordered from MOST verbose (index 0 = Trace) to LEAST verbose
    /// (index N-1 = Error). F2 moves the index DOWN (more details visible). Shift+F2
    /// moves it UP (fewer details).
    /// </summary>
    private static readonly LogLevel[] VerbosityLadder =
    {
        LogLevel.Trace, LogLevel.Debug, LogLevel.Information,
        LogLevel.Warning, LogLevel.Error
    };

    public static StatusBar Build(
        SplitPaneToplevel toplevel,
        TerminalGuiLoggerProvider logProvider,
        FindDialog findDialog)
    {
        ArgumentNullException.ThrowIfNull(toplevel);
        ArgumentNullException.ThrowIfNull(logProvider);
        ArgumentNullException.ThrowIfNull(findDialog);

        // Initialise the pane title with the current level so the user sees it from the start.
        toplevel.Logs.SetVisibleLevel(logProvider.CurrentMinimumLevel);

        void ApplyLevel(LogLevel next)
        {
            logProvider.SetMinimumLevel(next);
            toplevel.Logs.SetVisibleLevel(next);
        }

        // Secondary actions, available from the More menu and via global key bindings.
        var secondary = new List<MoreEntry>
        {
            new("Clear logs",            "Ctrl+L",  Key.L.WithCtrl,        toplevel.Logs.Clear),
            new("Clear REPL",            "Ctrl+K",  Key.K.WithCtrl,        toplevel.Repl.Clear),
            new("Toggle REPL pane",      "Ctrl+R",  Key.R.WithCtrl,        toplevel.ToggleReplVisible),
            new("Toggle REPL wrap",      "F3",      Key.F3,                toplevel.Repl.ToggleWrap),
            new("Toggle Tasks pane",     "Ctrl+T",  Key.T.WithCtrl,        toplevel.ToggleTasksVisible),
            new("Grow logs pane",        "Ctrl+↑",  Key.CursorUp.WithCtrl, () => toplevel.SetSplitRatio(toplevel.CurrentSplitRatio + 0.05)),
            new("Shrink logs pane",      "Ctrl+↓",  Key.CursorDown.WithCtrl, () => toplevel.SetSplitRatio(toplevel.CurrentSplitRatio - 0.05)),
            new("More log details",      "F2",      Key.F2,                () => ApplyLevel(MoreVerbose(logProvider.CurrentMinimumLevel))),
            new("Less log details",      "Shift+F2", Key.F2.WithShift,     () => ApplyLevel(LessVerbose(logProvider.CurrentMinimumLevel))),
        };

        // Bind the secondary keys globally — without this, hiding the Shortcut slots
        // also drops their keystroke handlers.
        InstallSecondaryKeyBindings(secondary);

        // Compact, must-see shortcut set on the StatusBar.
        var find       = MakeShortcut(Key.F.WithCtrl, "Find",       findDialog.Show, "Search logs");
        var cancelCmd  = MakeShortcut(Key.C.WithCtrl, "Cancel",     () => HandleCancelCurrentCommand(toplevel), "Cancel current command");
        var quit       = MakeShortcut(Key.Q.WithCtrl, "Quit",       () => HandleQuit(toplevel), "Exit");

        Shortcut? toggleLogs = null;
        toggleLogs = MakeShortcut(Key.G.WithCtrl, ToggleLabel(toplevel.IsLogsVisible), () =>
        {
            toplevel.ToggleLogsVisible();
            toggleLogs!.Text = ToggleLabel(toplevel.IsLogsVisible);
        }, "Show/hide logs pane");

        var more = MakeShortcut(Key.F1, "More…", () => ShowMoreMenu(secondary), "More commands");

        return new StatusBar(new[] { find, cancelCmd, quit, toggleLogs, more });
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "UI-shortcut fault barrier: a failing shortcut action is swallowed so it can never crash the Terminal.Gui key-dispatch loop.")]
    private static void InstallSecondaryKeyBindings(IReadOnlyList<MoreEntry> entries)
    {
        var snapshot = entries;
        Application.KeyDown += (_, key) =>
        {
            if (key.Handled) return;
            var match = snapshot.FirstOrDefault(entry => KeysMatch(key, entry.Key));
            if (match is not null)
            {
                try { match.Action(); }
                catch { /* swallow — UI shortcut should never crash the loop */ }
                key.Handled = true;
            }
        };
    }

    private static bool KeysMatch(Key actual, Key expected)
        => actual.KeyCode == expected.KeyCode;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "UI-shortcut fault barrier: a failing menu action is swallowed so it can never crash the Terminal.Gui loop.")]
    private static void ShowMoreMenu(IReadOnlyList<MoreEntry> entries)
    {
        var labels = new string[entries.Count + 1];
        for (var i = 0; i < entries.Count; i++)
            labels[i] = $"{entries[i].Label} ({entries[i].KeyLabel})";
        labels[^1] = "Cancel";

        var choice = MessageBox.Query(
            Application.Instance!,
            "More commands",
            "Choose a command:",
            labels);
        if (choice is null) return;
        var idx = choice.Value;
        if (idx < 0 || idx >= entries.Count) return;
        try { entries[idx].Action(); }
        catch { /* same rationale as InstallSecondaryKeyBindings */ }
    }

    private static void HandleQuit(SplitPaneToplevel toplevel)
    {
        var runner = toplevel.Runner;
        if (runner is { IsCommandRunning: true })
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

    private static void HandleCancelCurrentCommand(SplitPaneToplevel toplevel)
    {
        var runner = toplevel.Runner;
        if (runner is null || !runner.IsCommandRunning) return;
        runner.RequestCommandCancellation();
    }

    private static Shortcut MakeShortcut(Key key, string text, Action action, string help)
    {
        var s = new Shortcut(key, text, action, help);
        s.BindKeyToApplication = true;
        return s;
    }

    internal static string ToggleLabel(bool visible) => visible ? "Hide logs" : "Show logs";

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

    private sealed record MoreEntry(string Label, string KeyLabel, Key Key, Action Action);
}
