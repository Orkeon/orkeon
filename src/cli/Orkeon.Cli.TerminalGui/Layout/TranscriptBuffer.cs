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
internal readonly record struct TranscriptRow(long Line, string Text, int Start, int End);

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
/// Not thread-safe: the owning view touches it on the UI thread only.
/// </remarks>
internal sealed class TranscriptBuffer
{
    /// <summary>Columns between two tab stops (the <c>TextView</c> default).</summary>
    public const int TabWidth = 4;

    private sealed class Entry
    {
        public string Text = string.Empty;
        public readonly List<int> RowStarts = [0];
        public long FirstRow;
        public int Columns;

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
    public int MaxColumns => _widest.First?.Value.Columns ?? 0;

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
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = 0;
        var first = true;
        while (true)
        {
            var newline = normalized.IndexOf('\n', start);
            var fragment = newline < 0 ? normalized[start..] : normalized[start..newline];
            if (first && Held > 0)
            {
                if (fragment.Length > 0) ExtendLast(fragment);
                // Written to or closed by a newline, the open line is a line of the transcript.
                if (fragment.Length > 0 || newline >= 0) PromoteOpenTail();
            }
            else
            {
                AddLine(fragment, unit: true);
            }
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

    /// <summary>Appends each item as its own entry (a log entry): never extends the open line.</summary>
    public void AppendLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        PromoteOpenTail();
        foreach (var line in lines)
        {
            var parts = (line ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            for (var i = 0; i < parts.Length; i++) AddLine(parts[i], unit: i == 0);
        }
        Trim();
    }

    public void Clear()
    {
        _firstLineId += Held;
        _items.Clear();
        _widest.Clear();
        _head = 0;
        _units = 0;
        _openTail = false;
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
        return new TranscriptRow(_firstLineId + index - _head, entry.Text, start, end);
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
        foreach (var grapheme in GraphemeHelper.GetGraphemes(slice.Text[slice.Start..slice.End]))
        {
            var width = ColumnsOf(grapheme, columns);
            if (columns + width > column) break;
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
        for (var line = from.Line; line <= to.Line; line++)
        {
            var text = _items[(int)(line - _firstLineId) + _head].Text;
            var start = line == from.Line ? Math.Min(from.Offset, text.Length) : 0;
            var end = line == to.Line ? Math.Min(to.Offset, text.Length) : text.Length;
            if (line > from.Line) builder.Append('\n');
            if (end > start) builder.Append(text, start, end - start);
        }
        return builder.ToString();
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

    private void PromoteOpenTail()
    {
        if (!_openTail) return;
        _openTail = false;
        _items[^1].Unit = true;
        _units++;
    }

    private void AddLine(string text, bool unit)
    {
        var entry = new Entry { Text = text, Unit = unit };
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
            TrackWidth(_firstLineId + i - _head, entry.Columns);
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
            CharactersWrapped += text.Length - resumeFrom;
            var column = entry.Columns;
            foreach (var grapheme in GraphemeHelper.GetGraphemes(text[resumeFrom..])) column += ColumnsOf(grapheme, column);
            entry.Columns = column;
            return;
        }

        var starts = entry.RowStarts;
        if (resumeFrom < 0)
        {
            starts.Clear();
            starts.Add(0);
        }
        var index = starts[^1];
        CharactersWrapped += text.Length - index;
        var columns = 0;
        var peak = 0;
        var breakAt = -1;
        foreach (var grapheme in GraphemeHelper.GetGraphemes(text[index..]))
        {
            var w = ColumnsOf(grapheme, columns);
            var isSpace = grapheme is " " or "\t";
            var overflows = width > 0 && columns + w > width;
            // A space that overflows hangs at the end of its row (clipped when drawn) instead of
            // opening the next row with a blank.
            if (isSpace && overflows) w = 0;
            else if (overflows && columns > 0)
            {
                if (breakAt > starts[^1])
                {
                    starts.Add(breakAt);
                    columns = Measure(text, breakAt, index);
                }
                else
                {
                    starts.Add(index);
                    columns = 0;
                }
                breakAt = -1;
                w = ColumnsOf(grapheme, columns);
            }
            columns += w;
            if (columns > peak) peak = columns;
            index += grapheme.Length;
            if (isSpace) breakAt = index;
        }
        // The widest row: the line width with the wrap off (one row), at most the width with it on.
        entry.Columns = resumeFrom >= 0 ? Math.Max(entry.Columns, peak) : peak;
    }
}
