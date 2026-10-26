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
/// it adds (or the open last line it extends); the head is trimmed past <see cref="Capacity"/> lines
/// without touching the rest; a full re-wrap happens only when the wrap width or the wrap mode change.
/// </summary>
/// <remarks>
/// The model is a text stream: <see cref="Append"/> splits on <c>\n</c>, the first fragment extends the
/// open last line and each further fragment opens a new one, so <see cref="Text"/> round-trips what was
/// written. Lines carry absolute ids (they keep counting while the head is trimmed) and absolute first
/// rows, so a row lookup is a binary search and no line's rows move when another is added or dropped.
/// Not thread-safe: the owning view touches it on the UI thread only.
/// </remarks>
internal sealed class TranscriptBuffer
{
    private sealed class Entry
    {
        public string Text = string.Empty;
        public int[] RowStarts = [0];
        public long FirstRow;
        public int Columns;
    }

    // Lines live in _items[_head.._items.Count); the head is compacted when half the list is dead,
    // so trimming costs amortized O(1) per line.
    private readonly List<Entry> _items = [];
    private int _head;
    private long _firstLineId;
    private int _width;
    private bool _wrap = true;
    private int _maxColumns;

    public TranscriptBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    /// <summary>Maximum number of lines kept; older lines are dropped from the head.</summary>
    public int Capacity { get; }

    /// <summary>Lines currently held.</summary>
    public int LineCount => _items.Count - _head;

    /// <summary>Absolute id of the first line held.</summary>
    public long FirstLineId => _firstLineId;

    /// <summary>Display rows of the lines held, at the current width and wrap mode.</summary>
    public int RowCount
    {
        get
        {
            if (LineCount == 0) return 0;
            var first = _items[_head];
            var last = _items[^1];
            return (int)(last.FirstRow + last.RowStarts.Length - first.FirstRow);
        }
    }

    /// <summary>Widest line held, in columns (the content width when the wrap is off).</summary>
    public int MaxColumns => _maxColumns;

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
            if (first && LineCount > 0)
            {
                if (fragment.Length > 0) ExtendLast(fragment);
            }
            else
            {
                AddLine(fragment);
            }
            first = false;
            if (newline < 0) break;
            start = newline + 1;
            if (start == normalized.Length)
            {
                AddLine(string.Empty);
                break;
            }
        }
        Trim();
    }

    /// <summary>Appends each item as its own line (a log entry): never extends the open line.</summary>
    public void AppendLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        foreach (var line in lines)
        {
            var parts = (line ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            foreach (var part in parts) AddLine(part);
        }
        Trim();
    }

    public void Clear()
    {
        _firstLineId += LineCount;
        _items.Clear();
        _head = 0;
        _maxColumns = 0;
    }

    /// <summary>The row at <paramref name="row"/> (0-based in the content), or null past the end.</summary>
    public TranscriptRow? GetRow(int row)
    {
        if (row < 0 || row >= RowCount) return null;
        var index = IndexOfRow(row);
        var entry = _items[index];
        var local = (int)(_items[_head].FirstRow + row - entry.FirstRow);
        var start = entry.RowStarts[local];
        var end = local + 1 < entry.RowStarts.Length ? entry.RowStarts[local + 1] : entry.Text.Length;
        return new TranscriptRow(_firstLineId + index - _head, entry.Text, start, end);
    }

    /// <summary>The position under <paramref name="column"/> of <paramref name="row"/>, clamped to the content.</summary>
    public TranscriptPosition HitTest(int row, int column)
    {
        if (LineCount == 0) return new TranscriptPosition(_firstLineId, 0);
        if (row < 0) return new TranscriptPosition(_firstLineId, 0);
        if (row >= RowCount) return End;
        var slice = GetRow(row)!.Value;
        var offset = slice.Start;
        var columns = 0;
        foreach (var grapheme in GraphemeHelper.GetGraphemes(slice.Text[slice.Start..slice.End]))
        {
            var width = Math.Max(0, grapheme.GetColumns());
            if (columns + width > column) break;
            columns += width;
            offset += grapheme.Length;
        }
        return new TranscriptPosition(slice.Line, offset);
    }

    /// <summary>The end of the content (after the last char of the last line).</summary>
    public TranscriptPosition End
        => LineCount == 0
            ? new TranscriptPosition(_firstLineId, 0)
            : new TranscriptPosition(_firstLineId + LineCount - 1, _items[^1].Text.Length);

    /// <summary>The start of the content.</summary>
    public TranscriptPosition Start => new(_firstLineId, 0);

    /// <summary>Text between two positions (in either order), lines joined by <c>\n</c>.</summary>
    public string GetText(TranscriptPosition a, TranscriptPosition b)
    {
        if (LineCount == 0) return string.Empty;
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
        var last = _firstLineId + LineCount - 1;
        if (position.Line > last) return End;
        var length = _items[(int)(position.Line - _firstLineId) + _head].Text.Length;
        return position with { Offset = Math.Clamp(position.Offset, 0, length) };
    }

    private int EffectiveWidth => _wrap && _width > 0 ? _width : 0;

    private void AddLine(string text)
    {
        var entry = new Entry { Text = text };
        entry.FirstRow = LineCount == 0 ? 0 : _items[^1].FirstRow + _items[^1].RowStarts.Length;
        Wrap(entry);
        _items.Add(entry);
    }

    private void ExtendLast(string fragment)
    {
        var entry = _items[^1];
        entry.Text += fragment;
        Wrap(entry);
    }

    private void Trim()
    {
        var excess = LineCount - Capacity;
        if (excess <= 0) return;
        for (var i = 0; i < excess; i++) _items[_head + i] = null!;
        _head += excess;
        _firstLineId += excess;
        if (_head > 32 && _head * 2 > _items.Count)
        {
            _items.RemoveRange(0, _head);
            _head = 0;
        }
    }

    private void RewrapAll()
    {
        _maxColumns = 0;
        long row = LineCount == 0 ? 0 : _items[_head].FirstRow;
        for (var i = _head; i < _items.Count; i++)
        {
            var entry = _items[i];
            entry.FirstRow = row;
            Wrap(entry);
            row += entry.RowStarts.Length;
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

    // Word wrap of one line: break after the last space that fits, else hard-break at the width.
    private void Wrap(Entry entry)
    {
        var text = entry.Text;
        CharactersWrapped += text.Length;
        var width = EffectiveWidth;
        var starts = new List<int>(1) { 0 };
        var total = 0;
        var columns = 0;
        var breakAt = -1;
        var columnsAtBreak = 0;
        var index = 0;
        foreach (var grapheme in GraphemeHelper.GetGraphemes(text))
        {
            var raw = Math.Max(0, grapheme.GetColumns());
            var w = raw;
            var isSpace = grapheme == " ";
            var overflows = width > 0 && columns + w > width;
            // A space that overflows hangs at the end of its row (clipped when drawn) instead of
            // opening the next row with a blank.
            if (isSpace && overflows) w = 0;
            else if (overflows && columns > 0)
            {
                if (breakAt > starts[^1])
                {
                    starts.Add(breakAt);
                    columns -= columnsAtBreak;
                }
                else
                {
                    starts.Add(index);
                    columns = 0;
                }
                breakAt = -1;
            }
            columns += w;
            total += raw;
            index += grapheme.Length;
            if (isSpace)
            {
                breakAt = index;
                columnsAtBreak = columns;
            }
        }
        entry.RowStarts = [.. starts];
        entry.Columns = total;
        if (total > _maxColumns) _maxColumns = total;
    }
}
