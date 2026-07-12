using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Shared keyboard-clipboard helpers for the <em>active</em> (focused) pane: select-all
/// (Ctrl+A), copy the current selection to the OS clipboard (Ctrl+C) and paste into an
/// editable field (Ctrl+V).
/// <para>
/// Centralising these here keeps the three call sites consistent:
/// <list type="bullet">
///   <item><see cref="ReplPaneView"/> — the editable prompt input (A / C / V).</item>
///   <item><see cref="MouseClipboardTextView"/> — the read-only Logs / REPL-history views (A / C).</item>
///   <item><see cref="Orkeon.Cli.TerminalGui.Hosting.TerminalGuiHost"/> — arbitrates Ctrl+C between
///   "copy the focused selection" and "cancel the running command".</item>
/// </list>
/// </para>
/// The clipboard target is <see cref="TuiClipboard"/> — a hybrid that fans copies out to the OS
/// clipboard when available (xclip/X11, PowerShell on WSL, pbcopy on macOS), to the hosting
/// terminal via OSC 52 (reaches the host clipboard across Docker/SSH), and to an in-process
/// cache used as the paste fallback. The mouse auto-copy path uses the same surface, so
/// keyboard and mouse copy land in one place.
/// </summary>
internal static class PaneClipboard
{
    /// <summary>Strips the Ctrl/Alt/Shift modifiers, yielding the bare key (e.g. Ctrl+A → <see cref="KeyCode.A"/>).</summary>
    public static KeyCode BareKey(Key key)
        => key.KeyCode & ~(KeyCode.CtrlMask | KeyCode.AltMask | KeyCode.ShiftMask);

    /// <summary>True when <paramref name="key"/> is Ctrl + <paramref name="letter"/> (Shift state ignored).</summary>
    public static bool IsCtrl(Key key, KeyCode letter) => key.IsCtrl && BareKey(key) == letter;

    /// <summary>
    /// Selects all the text of a focused <see cref="TextField"/> / <see cref="TextView"/>.
    /// Returns false (no-op) for any other view type.
    /// </summary>
    public static bool SelectAll(View? view)
    {
        switch (view)
        {
            case TextField field:
                field.SelectAll();
                return true;
            case TextView text:
                text.SelectAll();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Copies the current selection of a focused <see cref="TextField"/> / <see cref="TextView"/>
    /// to <see cref="TuiClipboard"/> (OS clipboard + OSC 52 + in-process cache).
    /// <para>
    /// Returns <c>false</c> — without touching the clipboard — when there is no selection. Callers use
    /// this to fall back to other Ctrl+C semantics (the host cancels the running command instead).
    /// When a selection exists the method returns <c>true</c> even if the OS clipboard write itself is
    /// unavailable, so a populated selection always reads as "copy", never as "cancel".
    /// </para>
    /// </summary>
    public static bool CopySelection(View? view)
    {
        var selected = view switch
        {
            TextField field => field.SelectedText,
            TextView text => text.SelectedText,
            _ => null,
        };
        if (string.IsNullOrEmpty(selected))
            return false;

        TuiClipboard.Copy(selected);
        return true;
    }

    /// <summary>
    /// Pastes the best available clipboard content (<see cref="TuiClipboard.TryGetText"/>: OS
    /// clipboard, else the last in-TUI copy) into <paramref name="input"/> at the caret,
    /// preserving any existing text. No-op (returns false) for a read-only input or when every
    /// clipboard surface is empty.
    /// </summary>
    public static bool PasteInto(TextView input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.ReadOnly)
            return false;

        if (!TuiClipboard.TryGetText(out var text))
            return false;

        // TextView.InsertText honours the caret position and multi-line content (embedded "\n").
        input.InsertText(text);
        return true;
    }
}
