using System.Text;

namespace Orkeon.Cli.Abstractions.Console.LineEditing;

/// <summary>
/// A raw-mode <em>multi-line</em> editor for plain (non-Terminal.Gui) REPL input: char-by-char
/// editing, ↑/↓ history, Tab completion (<c>/command</c> + <c>@path</c> via
/// <see cref="IReplInputAssist"/>) and a growing multi-line draft, reaching parity with the
/// Terminal.Gui input pane. History persists across <see cref="ReadLine"/> calls (the editor is held
/// for the lifetime of the adapter).
/// </summary>
/// <remarks>
/// <para>
/// Rendering is inline, anchored at the cursor column where <see cref="ReadLine"/> begins (just after
/// the prompt the runner already wrote). The first logical line starts at that anchor; each subsequent
/// line is drawn at column 0 on the following terminal row, and the input box grows downward as lines
/// are added.
/// </para>
/// <para>
/// <b>Enter submits</b> the whole (possibly multi-line) buffer; <b>Ctrl+O inserts a newline</b>. Ctrl+O
/// is used rather than Shift+Enter / Ctrl+J because most terminals fold those into a plain Enter (LF),
/// so the editor can never see them as distinct — whereas Ctrl+O arrives as <c>ConsoleKey.O</c> with
/// the Control modifier. Alt+Enter is intentionally left untouched.
/// </para>
/// <para>
/// ↑/↓ recall command history only when the caret is on the first / last row of the draft; in the
/// middle of a multi-line draft they move the caret between lines. Home/End act on the current logical
/// line. Ambiguous Tab inserts the longest common prefix (it does not list candidates — listing would
/// disturb the inline anchor); see <c>project/tasks/repl-plain-mode-line-editor.md</c>.
/// </para>
/// </remarks>
public sealed class LineEditor
{
    private readonly ILineEditorConsole _console;
    private readonly IReplInputAssist? _assist;
    private readonly List<string> _history = new();

    public LineEditor(ILineEditorConsole console, IReplInputAssist? assist = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _assist = assist;
    }

    /// <summary>Submitted lines, oldest first (for tests/diagnostics).</summary>
    public IReadOnlyList<string> History => _history;

    /// <summary>
    /// Reads one entry with editing/history/completion. Returns the submitted text on Enter (with
    /// embedded <c>\n</c> for a multi-line draft), or <see langword="null"/> on EOF (Ctrl+D on an
    /// empty buffer).
    /// </summary>
    public string? ReadLine()
    {
        var st = new EditState { HistoryCursor = _history.Count };
        (st.AnchorLeft, st.AnchorTop) = _console.GetCursor();

        Render(st);

        while (true)
        {
            var key = _console.ReadKey();
            var outcome = Dispatch(st, key);
            if (outcome.Done)
                return outcome.Text;
        }
    }

    /// <summary>
    /// Routes one keystroke. Returns <see cref="ReadOutcome.Continue"/> to keep editing, or a
    /// terminal outcome (EOF → <see langword="null"/> text; Enter → the submitted text).
    /// </summary>
    private ReadOutcome Dispatch(EditState st, ConsoleKeyInfo key)
    {
        // Ctrl+D — EOF on an empty buffer, otherwise ignored.
        if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.D)
        {
            if (st.Buffer.Length == 0) { _console.WriteLine(string.Empty); return new ReadOutcome(true, null); }
            return ReadOutcome.Continue;
        }

        // Ctrl+O — insert a newline (the multi-line trigger; Enter submits). Terminals fold
        // Shift+Enter / Ctrl+J into Enter, so Ctrl+O is the only portable distinct key here.
        if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.O)
        {
            st.Buffer.Insert(st.Pos, '\n'); st.Pos++; Render(st);
            return ReadOutcome.Continue;
        }

        if (key.Key == ConsoleKey.Enter)
            return Submit(st);

        if (HandleEditingOrNavigation(st, key))
            return ReadOutcome.Continue;

        // Printable character → insert at the cursor.
        if (!char.IsControl(key.KeyChar) && key.KeyChar != '\0')
        {
            st.Buffer.Insert(st.Pos, key.KeyChar);
            st.Pos++;
            Render(st);
        }

        return ReadOutcome.Continue;
    }

    /// <summary>
    /// Handles the editing/navigation keys (Backspace, Delete, arrows, Home/End, Tab). Returns
    /// <see langword="true"/> when the key was consumed, <see langword="false"/> when it should fall
    /// through to printable-character insertion.
    /// </summary>
    private bool HandleEditingOrNavigation(EditState st, ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Backspace:
                if (st.Pos > 0) { st.Buffer.Remove(st.Pos - 1, 1); st.Pos--; Render(st); }
                return true;

            case ConsoleKey.Delete:
                if (st.Pos < st.Buffer.Length) { st.Buffer.Remove(st.Pos, 1); Render(st); }
                return true;

            case ConsoleKey.LeftArrow:
                if (st.Pos > 0) { st.Pos--; Render(st); }
                return true;

            case ConsoleKey.RightArrow:
                if (st.Pos < st.Buffer.Length) { st.Pos++; Render(st); }
                return true;

            case ConsoleKey.Home:
                st.Pos = LineStart(st.Buffer, st.Pos); Render(st);
                return true;

            case ConsoleKey.End:
                st.Pos = LineEnd(st.Buffer, st.Pos); Render(st);
                return true;

            case ConsoleKey.UpArrow:
                HandleHistoryOrCaret(st, atBoundary: CurrentRow(st.Buffer, st.Pos) == 0, direction: -1);
                return true;

            case ConsoleKey.DownArrow:
                HandleHistoryOrCaret(st, atBoundary: CurrentRow(st.Buffer, st.Pos) == LastRow(st.Buffer), direction: +1);
                return true;

            case ConsoleKey.Tab:
                TryComplete(st); Render(st);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// ↑/↓ on the first/last row recalls command history; in the middle of a multi-line draft it
    /// moves the caret between lines.
    /// </summary>
    private void HandleHistoryOrCaret(EditState st, bool atBoundary, int direction)
    {
        if (atBoundary) NavigateHistory(st, direction);
        else MoveCaretVertical(st, direction);
        Render(st);
    }

    /// <summary>
    /// Parks the cursor past the last visual row so the runner's output lands below the draft,
    /// records the line in history, and returns the submitted text.
    /// </summary>
    private ReadOutcome Submit(EditState st)
    {
        var text = st.Buffer.ToString();
        var end = BuildLayout(text, st.AnchorLeft, _console.Width, text.Length);
        _console.SetCursor(end.CaretCol, st.AnchorTop + end.CaretRow);
        _console.WriteLine(string.Empty);
        RecordHistory(text);
        return new ReadOutcome(true, text);
    }

    private void Render(EditState st)
    {
        // Slice the draft into visual rows the way the terminal would, but compute it ourselves so
        // cursor placement uses absolute SetCursor and never relies on terminal auto-wrap / \n.
        var layout = BuildLayout(st.Buffer.ToString(), st.AnchorLeft, _console.Width, st.Pos);
        var rows = layout.Rows;
        var lastRowEnds = st.LastRowEnds;

        for (var i = 0; i < rows.Count; i++)
        {
            var left = i == 0 ? st.AnchorLeft : 0;
            _console.SetCursor(left, st.AnchorTop + i);
            _console.Write(rows[i]);

            var curEnd = left + rows[i].Length;
            var prevEnd = i < lastRowEnds.Length ? lastRowEnds[i] : 0;
            if (prevEnd > curEnd)
                _console.Write(new string(' ', prevEnd - curEnd));
        }

        // Erase whole visual rows the previous (taller) render used that this one no longer fills.
        for (var i = rows.Count; i < lastRowEnds.Length; i++)
        {
            _console.SetCursor(0, st.AnchorTop + i);
            _console.Write(new string(' ', lastRowEnds[i]));
        }

        var newRowEnds = new int[rows.Count];
        for (var i = 0; i < rows.Count; i++)
            newRowEnds[i] = (i == 0 ? st.AnchorLeft : 0) + rows[i].Length;
        st.LastRowEnds = newRowEnds;

        _console.SetCursor(layout.CaretCol, st.AnchorTop + layout.CaretRow);
    }

    private readonly record struct ReadOutcome(bool Done, string? Text)
    {
        /// <summary>Keep editing — no terminal result yet.</summary>
        public static ReadOutcome Continue => new(false, null);
    }

    private void NavigateHistory(EditState st, int direction)
    {
        if (_history.Count == 0) return;

        // Entering navigation from the live draft: stash it so ↓ can restore it.
        if (st.HistoryCursor == _history.Count)
            st.Draft = st.Buffer.ToString();

        st.HistoryCursor = Math.Clamp(st.HistoryCursor + direction, 0, _history.Count);
        var value = st.HistoryCursor == _history.Count ? st.Draft : _history[st.HistoryCursor];

        st.Buffer.Clear();
        st.Buffer.Append(value);
        st.Pos = value.Length;
    }

    /// <summary>Moves the caret one logical line up/down, keeping the column where possible.</summary>
    private static void MoveCaretVertical(EditState st, int direction)
    {
        var text = st.Buffer.ToString();
        var lineStart = LineStart(st.Buffer, st.Pos);
        var col = st.Pos - lineStart;

        int targetStart;
        if (direction < 0)
        {
            if (lineStart == 0) return;                  // already on the first row
            targetStart = LineStart(st.Buffer, lineStart - 1);
        }
        else
        {
            var lineEnd = LineEnd(st.Buffer, st.Pos);
            if (lineEnd >= text.Length) return;          // already on the last row
            targetStart = lineEnd + 1;
        }

        var targetEnd = LineEnd(st.Buffer, targetStart);
        st.Pos = Math.Min(targetStart + col, targetEnd);
    }

    private void TryComplete(EditState st)
    {
        if (_assist is null) return;

        // Completion is scoped to the caret's current logical line, so it behaves the same on a
        // single-line command and inside a multi-line draft.
        var text = st.Buffer.ToString();
        var ls = LineStart(st.Buffer, st.Pos);
        var le = LineEnd(st.Buffer, st.Pos);
        var left = text[ls..st.Pos];
        var rest = text[st.Pos..le];

        if (ReplCompletion.CompleteLeft(left, _assist) is not { } outcome)
            return;

        st.Buffer.Clear();
        st.Buffer.Append(text[..ls]).Append(outcome.NewLeft).Append(rest).Append(text[le..]);
        st.Pos = ls + outcome.NewLeft.Length;
    }

    private void RecordHistory(string line)
    {
        if (line.Trim().Length == 0) return;
        if (_history.Count == 0 || !string.Equals(_history[^1], line, StringComparison.Ordinal))
            _history.Add(line);
    }

    // ── visual layout / line geometry ───────────────────────────────────────────────────────

    private readonly record struct Layout(List<string> Rows, int CaretRow, int CaretCol);

    /// <summary>
    /// Splits <paramref name="text"/> into the visual rows the terminal shows, mirroring its wrapping:
    /// hard breaks at <c>\n</c> and soft breaks when a row fills the terminal <paramref name="width"/>.
    /// Only the first visual row starts at <paramref name="anchorLeft"/> (just after the prompt); every
    /// subsequent row — whether from a wrap or a newline — starts at column 0. Also reports the caret's
    /// (row, absolute-column) for the buffer index <paramref name="pos"/>, computed by the same walk so
    /// it always agrees with the rendered rows.
    /// </summary>
    private static Layout BuildLayout(string text, int anchorLeft, int width, int pos)
    {
        var w = Math.Max(1, width);
        var rows = new List<string> { string.Empty };
        var sb = new StringBuilder();
        var row = 0;
        var col = anchorLeft;          // absolute column; only row 0 is offset by the prompt
        var caretRow = 0;
        var caretCol = anchorLeft;

        void NewRow()
        {
            rows[row] = sb.ToString();
            sb.Clear();
            rows.Add(string.Empty);
            row++;
            col = 0;
        }

        for (var i = 0; i <= text.Length; i++)
        {
            if (i == pos)
            {
                // A caret resting exactly on a full row's edge shows at the start of the next row
                // (where the next char would wrap to), not off-screen at column == width.
                if (col >= w) { caretRow = row + 1; caretCol = 0; }
                else { caretRow = row; caretCol = col; }
            }
            if (i == text.Length) break;

            var c = text[i];
            if (c == '\n')
            {
                NewRow();
            }
            else
            {
                if (col >= w) NewRow();   // soft wrap before placing the next char
                sb.Append(c);
                col++;
            }
        }
        rows[row] = sb.ToString();

        return new Layout(rows, caretRow, caretCol);
    }

    /// <summary>Index of the first char of the logical line containing <paramref name="pos"/>.</summary>
    private static int LineStart(StringBuilder buffer, int pos)
    {
        var i = pos - 1;
        while (i >= 0 && buffer[i] != '\n') i--;
        return i + 1;
    }

    /// <summary>Index of the trailing newline (or buffer end) of the line containing <paramref name="pos"/>.</summary>
    private static int LineEnd(StringBuilder buffer, int pos)
    {
        var i = pos;
        while (i < buffer.Length && buffer[i] != '\n') i++;
        return i;
    }

    private static int CurrentRow(StringBuilder buffer, int pos)
    {
        var row = 0;
        for (var i = 0; i < pos && i < buffer.Length; i++)
            if (buffer[i] == '\n') row++;
        return row;
    }

    private static int LastRow(StringBuilder buffer)
    {
        var row = 0;
        for (var i = 0; i < buffer.Length; i++)
            if (buffer[i] == '\n') row++;
        return row;
    }

    private sealed class EditState
    {
        public StringBuilder Buffer { get; } = new();
        public int Pos { get; set; }
        public int HistoryCursor { get; set; }
        public string Draft { get; set; } = string.Empty;

        /// <summary>Column where <see cref="ReadLine"/> began (just after the prompt) — row 0 anchor.</summary>
        public int AnchorLeft { get; set; }

        /// <summary>Terminal row where the draft's first visual row is drawn.</summary>
        public int AnchorTop { get; set; }

        /// <summary>
        /// Absolute end column of each visual row in the previous render, so a shrinking draft erases
        /// its own remnants (both within a row and on rows it no longer occupies).
        /// </summary>
        public int[] LastRowEnds { get; set; } = Array.Empty<int>();
    }
}
