using Terminal.Gui.Drawing;
using Terminal.Gui.Text;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>A position in a <see cref="TranscriptBuffer"/>: an absolute line id and a char offset in that line.</summary>
internal readonly record struct TranscriptPosition(long Line, int Offset) : IComparable<TranscriptPosition>
{
    public int CompareTo(TranscriptPosition other)
        => Line != other.Line ? Line.CompareTo(other.Line) : Offset.CompareTo(other.Offset);
}

/// <summary>One display row: the line it belongs to and the char range of that line it shows.</summary>
/// <remarks>
/// The display comes with it: <see cref="Indent"/> blank columns before the text, the runs of the whole line,
/// and the role painted over the row.
/// </remarks>
internal readonly record struct TranscriptRow(long Line, string Text, int Start, int End)
{
    /// <summary>Blank columns before the text of this row (a hanging indent).</summary>
    public int Indent { get; init; }

    /// <summary>The styled runs of the whole line, sorted; char offsets are the line's.</summary>
    public IReadOnlyList<TranscriptRun> Runs { get; init; } = [];

    /// <summary>The role painted over the whole row.</summary>
    public PaletteRole? RowRole { get; init; }
}

/// <summary>
/// Line-based, bounded model of a read-only transcript (issue #196). Appending wraps only the lines
/// it adds (or the last row of the open line it extends); the head is trimmed past
/// <see cref="Capacity"/> lines or entries without touching the rest; a full re-wrap happens only when
/// the wrap width or the wrap mode change.
/// </summary>
/// <remarks>
/// The model is a text stream: <see cref="Append"/> splits on <c>\n</c>, the first fragment extends the
/// open last line and each further fragment opens a new one, so <see cref="Text"/> round-trips what was
/// written. Lines carry absolute ids (they keep counting while the head is trimmed) and absolute first
/// rows, so a row lookup is a binary search and no line's rows move when another is added or dropped.
/// The capacity counts the lines of <see cref="Append"/> and the entries of <see cref="AppendLines"/>
/// (an entry with several lines is kept or dropped whole); the empty line opened after a final
/// <c>\n</c> does not count. A tab advances to the next multiple of <see cref="TabWidth"/> columns of its
/// row, as the <c>TextView</c> it replaces drew it; positions and copies keep the tab itself.
/// <see cref="AppendStyled"/> adds lines that carry their display (<see cref="TranscriptLine"/>); the text of a
/// line stays what the wrap, the positions and the trim work on, whatever is painted over it.
/// Not thread-safe: the owning view touches it on the UI thread only.
/// </remarks>
internal sealed class TranscriptBuffer
{
    /// <summary>Columns between two tab stops (the <c>TextView</c> default).</summary>
    public const int TabWidth = 4;

    // A hanging indent is dropped when it would leave a continuation row narrower than this.
    private const int MinContinuationColumns = 8;

    private sealed class Entry
    {
        public string Text = string.Empty;

        // The display of a styled line; null for every line of the stream.
        public TranscriptLine? Style;
        public readonly List<int> RowStarts = [0];
        public long FirstRow;
        public int Columns;

        // With the wrap off: where the last grapheme starts and its column, so an extension re-measures
        // from there (a ZWJ or VS16 sequence split across two pieces is one grapheme, not two).
        public int TailStart;
        public int TailColumn;

        // Counts against the capacity: the first line of an entry, or a line of the stream. False for
        // the continuation lines of a multi-line entry and for the empty line opened after a final \n.
        public bool Unit;
    }

    // Lines live in _items[_head.._items.Count); the head is compacted when half the list is dead,
    // so trimming costs amortized O(1) per line.
    private readonly List<Entry> _items = [];

    // Sliding-window maximum of the line widths: ids with decreasing columns, so MaxColumns follows
    // the trim at amortized O(1) per line.
    private readonly LinkedList<(long Id, int Columns)> _widest = new();
    private int _head;
    private long _firstLineId;
    private int _units;

    // The last line is the empty line Append opened after a final \n: not counted until written to.
    private bool _openTail;

    // The last entry is an unfinished line of a stream: the next ReplacePartial takes it back. Its width stays out
    // of _widest, which only knows lines that grow, until another write makes it a line like any other.
    private bool _partial;
    private int _partialColumns;
    private int _width;
    private bool _wrap = true;

    public TranscriptBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    /// <summary>Maximum number of lines (or entries) kept; older ones are dropped from the head.</summary>
    public int Capacity { get; }

    /// <summary>
    /// Lines (or entries) counted against the capacity: a stream line, or an entry of
    /// <see cref="AppendLines"/> whatever its line count; the empty line after a final <c>\n</c> is not one.
    /// </summary>
    public int LineCount => _units;

    // Lines physically held, the open empty line and the continuation lines included.
    private int Held => _items.Count - _head;

    /// <summary>Absolute id of the first line held.</summary>
    public long FirstLineId => _firstLineId;

    /// <summary>Display rows of the lines held, at the current width and wrap mode.</summary>
    public int RowCount
    {
        get
        {
            if (Held == 0) return 0;
            var first = _items[_head];
            var last = _items[^1];
            return (int)(last.FirstRow + last.RowStarts.Count - first.FirstRow);
        }
    }

    /// <summary>Widest line held, in columns (the content width when the wrap is off).</summary>
    public int MaxColumns => Math.Max(_widest.First?.Value.Columns ?? 0, _partialColumns);

    /// <summary>Work counter: characters wrapped since construction (a line wrapped twice counts twice).</summary>
    public long CharactersWrapped { get; private set; }

    /// <summary>Wrap width in columns; zero or less wraps nothing.</summary>
    public int Width => _width;

    public bool WordWrap => _wrap;

    /// <summary>The whole text, lines joined by <c>\n</c>.</summary>
    public string Text
    {
        get
        {
            var builder = new System.Text.StringBuilder();
            for (var i = _head; i < _items.Count; i++)
            {
                if (i > _head) builder.Append('\n');
                builder.Append(_items[i].Text);
            }
            return builder.ToString();
        }
    }

    /// <summary>Columns a grapheme takes at <paramref name="column"/> of its row: a tab reaches the next stop.</summary>
    public static int ColumnsOf(string grapheme, int column)
        => grapheme == "\t" ? TabWidth - (column % TabWidth) : Math.Max(0, grapheme.GetColumns());

    /// <summary>
    /// Columns a grapheme takes at <paramref name="column"/> of a row laid out by this buffer: as
    /// <see cref="ColumnsOf"/>, except that with the wrap on a tab whose stop lies past the row end is
    /// clipped to the columns left on its row. Wrapping, drawing and hit-testing all measure with it.
    /// </summary>
    /// <param name="grapheme">The grapheme measured.</param>
    /// <param name="column">Its column in the row's text.</param>
    /// <param name="indent">The blank columns before the row's text.</param>
    public int ColumnsAt(string grapheme, int column, int indent = 0)
    {
        var w = ColumnsOf(grapheme, column);
        var width = EffectiveWidth > 0 ? EffectiveWidth - indent : 0;
        return grapheme == "\t" && width > 0 && column < width && column + w > width ? width - column : w;
    }

    /// <summary>Sets the wrap width and mode; re-wraps every line only when either changed.</summary>
    public void SetLayout(int width, bool wordWrap)
    {
        if (width == _width && wordWrap == _wrap) return;
        var before = EffectiveWidth;
        _width = width;
        _wrap = wordWrap;
        if (EffectiveWidth == before) return;
        RewrapAll();
    }

    /// <summary>Appends stream text: the first fragment extends the open last line, each <c>\n</c> opens a new one.</summary>
    public void Append(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return;
        SettlePartial();
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = 0;
        var first = true;
        while (true)
        {
            var newline = normalized.IndexOf('\n', start);
            var fragment = newline < 0 ? normalized[start..] : normalized[start..newline];
            AppendFragment(fragment, first, newline >= 0);
            first = false;
            if (newline < 0) break;
            start = newline + 1;
            if (start == normalized.Length)
            {
                AddLine(string.Empty, unit: false);
                _openTail = true;
                break;
            }
        }
        Trim();
    }

    private void AppendFragment(string fragment, bool first, bool closedByNewline)
    {
        if (first && Held > 0)
        {
            if (fragment.Length > 0) ExtendLast(fragment);
            // Written to or closed by a newline, the open line is a line of the transcript.
            if (fragment.Length > 0 || closedByNewline) PromoteOpenTail();
        }
        else
        {
            AddLine(fragment, unit: true);
        }
    }

    /// <summary>Appends each item as its own entry (a log entry): never extends the open line.</summary>
    public void AppendLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        SettlePartial();
        PromoteOpenTail();
        foreach (var line in lines)
        {
            var parts = (line ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            for (var i = 0; i < parts.Length; i++) AddLine(parts[i], unit: i == 0);
        }
        Trim();
    }

    /// <summary>
    /// Appends lines that carry their display. They take the open empty line, so no blank row is left
    /// before them, and they leave a new one: stream text written next starts on its own line.
    /// </summary>
    public void AppendStyled(IReadOnlyList<TranscriptLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0) return;
        SettlePartial();
        var first = 0;
        if (_openTail)
        {
            FillOpenTail(lines[0]);
            first = 1;
        }
        for (var i = first; i < lines.Count; i++) AddLine(OnOneRow(lines[i].Text), unit: true, lines[i]);
        AddLine(string.Empty, unit: false);
        _openTail = true;
        Trim();
    }

    /// <summary>
    /// Replaces the unfinished line of a stream, when one is shown, with the lines that finish it and the
    /// unfinished line that follows them. Any other write in between makes that line a line like any other.
    /// </summary>
    public void ReplacePartial(IReadOnlyList<TranscriptLine> lines, string partial)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(partial);
        partial = OnOneRow(partial);
        if (_partial && lines.Count == 0 && partial.Length > 0 && partial.StartsWith(_items[^1].Text, StringComparison.Ordinal))
        {
            // The line goes on: only what it adds is wrapped, not all of it at each delta.
            var grown = _items[^1];
            var from = grown.Text.Length;
            grown.Text = partial;
            Wrap(grown, resumeFrom: from);
            _partialColumns = grown.Columns;
            return;
        }
        if (_partial)
        {
            var entry = _items[^1];
            entry.Text = string.Empty;
            entry.Style = null;
            Wrap(entry, resumeFrom: -1);
            if (entry.Unit)
            {
                entry.Unit = false;
                _units--;
            }
            _openTail = true;
            _partial = false;
            _partialColumns = 0;
        }
        AppendStyled(lines);
        if (partial.Length == 0) return;
        if (_openTail) PromoteOpenTail();
        else AddLine(string.Empty, unit: true);
        var tail = _items[^1];
        tail.Text = partial;
        Wrap(tail, resumeFrom: -1);
        _partial = true;
        _partialColumns = tail.Columns;
        Trim();
    }

    // A line holds no line break, whoever wrote it (a decoded entity, a task's description): each reads as a blank,
    // so the runs keep their offsets and a text never takes the UI loop down.
    private static string OnOneRow(string text) => text.Replace('\n', ' ').Replace('\r', ' ');

    private void FillOpenTail(TranscriptLine line)
    {
        var entry = _items[^1];
        entry.Text = OnOneRow(line.Text);
        entry.Style = line;
        Wrap(entry, resumeFrom: -1);
        PromoteOpenTail();
        TrackWidth(_firstLineId + Held - 1, entry.Columns);
    }

    public void Clear()
    {
        _partial = false;
        _partialColumns = 0;
        _firstLineId += Held;
        _items.Clear();
        _widest.Clear();
        _head = 0;
        _units = 0;
        _openTail = false;
    }

    /// <summary>
    /// The first line of the nearest block painted in <paramref name="role"/> that starts above
    /// <paramref name="row"/>; null when none did, or when that block is what the row shows first.
    /// </summary>
    public string? LineAbove(int row, PaletteRole role)
    {
        if (row < 0 || row >= RowCount) return null;
        var index = IndexOfRow(row);
        var startsThere = _items[index].FirstRow - _items[_head].FirstRow == row;
        var opensBlock = _items[index].Style?.RowRole == role && (index == _head || _items[index - 1].Style?.RowRole != role);
        if (startsThere && opensBlock) return null;
        if (startsThere) index--;
        while (index >= _head && _items[index].Style?.RowRole != role) index--;
        if (index < _head) return null;
        while (index > _head && _items[index - 1].Style?.RowRole == role) index--;
        return _items[index].Text;
    }

    /// <summary>The row at <paramref name="row"/> (0-based in the content), or null past the end.</summary>
    public TranscriptRow? GetRow(int row)
    {
        if (row < 0 || row >= RowCount) return null;
        var index = IndexOfRow(row);
        var entry = _items[index];
        var local = (int)(_items[_head].FirstRow + row - entry.FirstRow);
        var start = entry.RowStarts[local];
        var end = local + 1 < entry.RowStarts.Count ? entry.RowStarts[local + 1] : entry.Text.Length;
        return new TranscriptRow(_firstLineId + index - _head, entry.Text, start, end)
        {
            Indent = local > 0 ? HangOf(entry) : 0,
            Runs = entry.Style?.Runs ?? [],
            RowRole = entry.Style?.RowRole,
        };
    }

    /// <summary>The position under <paramref name="column"/> of <paramref name="row"/>, clamped to the content.</summary>
    public TranscriptPosition HitTest(int row, int column)
    {
        if (Held == 0) return new TranscriptPosition(_firstLineId, 0);
        if (row < 0) return new TranscriptPosition(_firstLineId, 0);
        if (row >= RowCount) return End;
        var slice = GetRow(row)!.Value;
        var offset = slice.Start;
        var columns = 0;
        var textColumn = column - slice.Indent;
        foreach (var grapheme in GraphemeHelper.GetGraphemes(slice.Text[slice.Start..slice.End]))
        {
            var width = ColumnsAt(grapheme, columns, slice.Indent);
            if (columns + width > textColumn) break;
            columns += width;
            offset += grapheme.Length;
        }
        return new TranscriptPosition(slice.Line, offset);
    }

    /// <summary>The end of the content (after the last char of the last line).</summary>
    public TranscriptPosition End
        => Held == 0
            ? new TranscriptPosition(_firstLineId, 0)
            : new TranscriptPosition(_firstLineId + Held - 1, _items[^1].Text.Length);

    /// <summary>The start of the content.</summary>
    public TranscriptPosition Start => new(_firstLineId, 0);

    /// <summary>Text between two positions (in either order), lines joined by <c>\n</c>.</summary>
    public string GetText(TranscriptPosition a, TranscriptPosition b)
    {
        if (Held == 0) return string.Empty;
        var (from, to) = a.CompareTo(b) <= 0 ? (Clamp(a), Clamp(b)) : (Clamp(b), Clamp(a));
        if (from.CompareTo(to) == 0) return string.Empty;
        var builder = new System.Text.StringBuilder();
        var opened = false;
        for (var line = from.Line; line <= to.Line; line++)
        {
            var entry = _items[(int)(line - _firstLineId) + _head];
            var start = line == from.Line ? Math.Min(from.Offset, entry.Text.Length) : 0;
            var end = line == to.Line ? Math.Min(to.Offset, entry.Text.Length) : entry.Text.Length;
            var whole = start == 0 && end == entry.Text.Length;
            // What is only drawn (a table's border) has no line in a copy.
            if (whole && entry.Text.Length > 0 && entry.Style?.CopySource is { Length: 0 }) continue;
            if (opened) builder.Append('\n');
            opened = true;
            AppendCopy(builder, entry, start, end, whole && from.Line < to.Line, whole);
        }
        return builder.ToString();
    }

    private static void AppendCopy(System.Text.StringBuilder builder, Entry entry, int start, int end, bool withLead, bool whole)
    {
        if (withLead && entry.Style?.CopyLead is { } lead) builder.Append(lead).Append('\n');
        if (entry.Style?.CopySource is { } source && whole) builder.Append(source);
        else if (end > start) builder.Append(entry.Text, start, end - start);
    }

    private TranscriptPosition Clamp(TranscriptPosition position)
    {
        if (position.Line < _firstLineId) return Start;
        var last = _firstLineId + Held - 1;
        if (position.Line > last) return End;
        var length = _items[(int)(position.Line - _firstLineId) + _head].Text.Length;
        return position with { Offset = Math.Clamp(position.Offset, 0, length) };
    }

    private int EffectiveWidth => _wrap && _width > 0 ? _width : 0;

    // Any other write makes the unfinished line a line like any other: its width now counts for good.
    private void SettlePartial()
    {
        if (_partial) TrackWidth(_firstLineId + Held - 1, _items[^1].Columns);
        _partial = false;
        _partialColumns = 0;
    }

    private void PromoteOpenTail()
    {
        if (!_openTail) return;
        _openTail = false;
        _items[^1].Unit = true;
        _units++;
    }

    private void AddLine(string text, bool unit, TranscriptLine? style = null)
    {
        var entry = new Entry { Text = text, Unit = unit, Style = style };
        entry.FirstRow = Held == 0 ? 0 : _items[^1].FirstRow + _items[^1].RowStarts.Count;
        Wrap(entry, resumeFrom: -1);
        _items.Add(entry);
        _openTail = false;
        if (unit) _units++;
        TrackWidth(_firstLineId + Held - 1, entry.Columns);
    }

    private void ExtendLast(string fragment)
    {
        var entry = _items[^1];
        var from = entry.Text.Length;
        entry.Text += fragment;
        Wrap(entry, resumeFrom: from);
        TrackWidth(_firstLineId + Held - 1, entry.Columns);
    }

    // The last line's width only grows: drop the narrower lines it now dominates, itself included.
    private void TrackWidth(long id, int columns)
    {
        if (_widest.Last is { } last && last.Value.Id == id) _widest.RemoveLast();
        while (_widest.Last is { } tail && tail.Value.Columns <= columns) _widest.RemoveLast();
        _widest.AddLast((id, columns));
    }

    private void Trim()
    {
        while (_units > Capacity)
        {
            // Drop the head entry whole: its first line and its continuation lines. Another unit
            // follows it (the count is still above the capacity), so the loop never empties the list.
            do
            {
                if (_items[_head].Unit) _units--;
                _items[_head] = null!;
                _head++;
                if (_widest.First is { } widest && widest.Value.Id == _firstLineId) _widest.RemoveFirst();
                _firstLineId++;
            }
            while (_head < _items.Count && !_items[_head].Unit);
        }
        if (_head > 32 && _head * 2 > _items.Count)
        {
            _items.RemoveRange(0, _head);
            _head = 0;
        }
    }

    private void RewrapAll()
    {
        _widest.Clear();
        long row = Held == 0 ? 0 : _items[_head].FirstRow;
        for (var i = _head; i < _items.Count; i++)
        {
            var entry = _items[i];
            entry.FirstRow = row;
            Wrap(entry, resumeFrom: -1);
            row += entry.RowStarts.Count;
            if (_partial && i == _items.Count - 1) _partialColumns = entry.Columns;
            else TrackWidth(_firstLineId + i - _head, entry.Columns);
        }
    }

    private int IndexOfRow(int row)
    {
        var target = _items[_head].FirstRow + row;
        int lo = _head, hi = _items.Count - 1;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo + 1) / 2);
            if (_items[mid].FirstRow <= target) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    // Columns of text[from..to) laid from the start of a row.
    private static int Measure(string text, int from, int to)
    {
        var columns = 0;
        foreach (var grapheme in GraphemeHelper.GetGraphemes(text[from..to])) columns += ColumnsOf(grapheme, columns);
        return columns;
    }

    private int HangOf(Entry entry)
    {
        var hang = entry.Style?.HangingIndent ?? 0;
        return EffectiveWidth >= hang + MinContinuationColumns ? hang : 0;
    }

    // Word wrap of one line: break after the last space that fits, else hard-break at the width.
    // resumeFrom >= 0 is the length the line had before an extension: the rows before its last one
    // cannot change, so only that row and the new text are wrapped again (with the wrap off there is
    // one row, and only the new text is measured).
    private void Wrap(Entry entry, int resumeFrom)
    {
        var text = entry.Text;
        var width = EffectiveWidth;
        if (resumeFrom >= 0 && width == 0)
        {
            WrapResumedUnwrapped(entry);
            return;
        }

        var starts = entry.RowStarts;
        if (resumeFrom < 0)
        {
            starts.Clear();
            starts.Add(0);
            entry.TailStart = 0;
            entry.TailColumn = 0;
        }
        var run = new WrapRun { Width = width, Hang = HangOf(entry), Starts = starts, Index = starts[^1], BreakAt = -1 };
        CharactersWrapped += text.Length - run.Index;
        foreach (var grapheme in GraphemeHelper.GetGraphemes(text[run.Index..]))
            WrapStep(entry, run, grapheme);
        // The widest row: the line width with the wrap off (one row), at most the width with it on.
        entry.Columns = resumeFrom >= 0 ? Math.Max(entry.Columns, run.Peak) : run.Peak;
    }

    // Resume at the start of the previous last grapheme: the new text may extend it.
    private void WrapResumedUnwrapped(Entry entry)
    {
        var text = entry.Text;
        var from = entry.TailStart;
        var column = entry.TailColumn;
        CharactersWrapped += text.Length - from;
        foreach (var grapheme in GraphemeHelper.GetGraphemes(text[from..]))
        {
            entry.TailStart = from;
            entry.TailColumn = column;
            column += ColumnsOf(grapheme, column);
            from += grapheme.Length;
        }
        entry.Columns = column;
    }

    private sealed class WrapRun
    {
        public int Width { get; init; }
        public int Hang { get; init; }
        public required List<int> Starts { get; init; }
        public int Index { get; set; }
        public int Columns { get; set; }
        public int Peak { get; set; }
        public int BreakAt { get; set; }
    }

    private static void WrapStep(Entry entry, WrapRun run, string grapheme)
    {
        if (run.Width == 0)
        {
            entry.TailStart = run.Index;
            entry.TailColumn = run.Columns;
        }
        var w = ColumnsOf(grapheme, run.Columns);
        var isSpace = grapheme is " " or "\t";
        var rowWidth = run.Starts.Count > 1 ? run.Width - run.Hang : run.Width;
        var overflows = run.Width > 0 && run.Columns + w > rowWidth;
        // A space or a tab that overflows from the row end hangs there (clipped when drawn)
        // instead of opening the next row with a blank. A tab that overflows before the row end
        // is clipped to the columns left (ColumnsAt), so what follows it opens the next row.
        if (isSpace && overflows) w = Math.Max(0, rowWidth - run.Columns);
        else if (overflows && run.Columns > 0) w = OpenNextRow(entry.Text, run, grapheme);
        run.Columns += w;
        run.Peak = Math.Max(run.Peak, run.Columns + (run.Starts.Count > 1 ? run.Hang : 0));
        run.Index += grapheme.Length;
        if (isSpace) run.BreakAt = run.Index;
    }

    // The word carried over and the grapheme that carried it must fit the row they land on,
    // narrower when the line hangs. Returns the width of the grapheme on its new row.
    private static int OpenNextRow(string text, WrapRun run, string grapheme)
    {
        var starts = run.Starts;
        var carried = run.BreakAt > starts[^1] ? Measure(text, run.BreakAt, run.Index) : 0;
        if (run.BreakAt > starts[^1] && carried + ColumnsOf(grapheme, carried) <= run.Width - run.Hang)
        {
            starts.Add(run.BreakAt);
            run.Columns = carried;
        }
        else
        {
            starts.Add(run.Index);
            run.Columns = 0;
        }
        run.BreakAt = -1;
        return ColumnsOf(grapheme, run.Columns);
    }
}
