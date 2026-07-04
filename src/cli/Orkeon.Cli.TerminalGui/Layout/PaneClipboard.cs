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
/// The clipboard target is the OS clipboard (<see cref="Application.Clipboard"/>: xclip on Linux,
/// PowerShell on WSL, pbcopy on macOS, native API on Windows) — the same surface the mouse
/// auto-copy path uses, so keyboard and mouse copy land in one place.
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
    /// to the OS clipboard.
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

        Application.Clipboard?.TrySetClipboardData(selected);
        return true;
    }

    /// <summary>
    /// Pastes the OS clipboard contents into <paramref name="input"/> at the caret, preserving any
    /// existing text. No-op (returns false) for a read-only input, an unavailable clipboard, or empty
    /// clipboard contents.
    /// </summary>
    public static bool PasteInto(TextView input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.ReadOnly)
            return false;

        var clipboard = Application.Clipboard;
        if (clipboard is null)
            return false;
        if (!clipboard.TryGetClipboardData(out var text) || string.IsNullOrEmpty(text))
            return false;

        // TextView.InsertText honours the caret position and multi-line content (embedded "\n").
        input.InsertText(text);
        return true;
    }
}
