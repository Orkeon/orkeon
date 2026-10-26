using System.Drawing;
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
            case TranscriptView transcript:
                transcript.SelectAll();
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
            TranscriptView transcript => transcript.SelectedText,
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
    /// preserving any existing text; the caret ends after the pasted text. No-op (returns false)
    /// for a read-only input or when every clipboard surface is empty.
    /// <para>
    /// The text is recomposed in one block and set at once. <c>TextView.InsertText</c> inserts one
    /// rune at a time, each insertion rebuilding the model and raising the change event: a paste of
    /// fifty transcript lines froze the TUI for twelve seconds, two hundred lines for minutes. Setting
    /// the text once takes a fraction of a second whatever the size.
    /// </para>
    /// </summary>
    public static bool PasteInto(TextView input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.ReadOnly)
            return false;

        if (!TuiClipboard.TryGetText(out var text))
            return false;

        var pasted = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = input.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var row = Math.Clamp(input.CurrentRow, 0, lines.Length - 1);
        var line = lines[row];
        var at = CharOffsetOfRune(line, input.CurrentColumn);
        lines[row] = string.Concat(line.AsSpan(0, at), pasted, line.AsSpan(at));
        input.Text = string.Join('\n', lines);

        var pastedLines = pasted.Split('\n');
        var lastPastedLine = pastedLines[^1];
        var column = pastedLines.Length == 1
            ? RuneCount(line.AsSpan(0, at)) + RuneCount(lastPastedLine)
            : RuneCount(lastPastedLine);
        input.InsertionPoint = new Point(column, row + pastedLines.Length - 1);
        return true;
    }

    /// <summary>The UTF-16 offset of the rune at <paramref name="runeIndex"/> in <paramref name="line"/>, the line's length past its end.</summary>
    private static int CharOffsetOfRune(string line, int runeIndex)
    {
        var offset = 0;
        var seen = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (seen++ >= runeIndex)
                break;
            offset += rune.Utf16SequenceLength;
        }
        return Math.Min(offset, line.Length);
    }

    private static int RuneCount(ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var _ in text.EnumerateRunes())
            count++;
        return count;
    }
}
