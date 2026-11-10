using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// The selection panel, without a terminal: which option is selected, what the sideways control holds, and the
/// rows it paints for a width and a height. The pane translates keys into its methods and paints its rows.
/// </summary>
internal sealed class InlinePanelModel : IPanelRows
{
    private const int MaxVisibleChoices = 10;
    private const int MinVisibleChoices = 3;
    private const int MaxDetailRows = 6;
    private const int LabelColumnCap = 40;

    private readonly ConsoleChoiceRequest _request;
    private readonly GlyphSet _glyphs;
    private int _controlIndex;

    public InlinePanelModel(ConsoleChoiceRequest request, GlyphSet glyphs)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(glyphs);
        if (request.Choices.Count == 0)
            throw new ArgumentException("A choice needs at least one option.", nameof(request));
        _request = request;
        _glyphs = glyphs;
        Selected = OpeningIndex(request);
        if (request.Control is { } control)
            _controlIndex = Math.Max(0, IndexOf(control.Values, control.Value));
    }

    /// <summary>The index of the option selected.</summary>
    public int Selected { get; private set; }

    /// <summary>Whether Esc leaves the panel without an answer.</summary>
    public bool Cancellable => _request.Cancellable;

    /// <summary>What Enter answers now.</summary>
    public ConsoleChoiceAnswer Answer
        => new(_request.Choices[Selected].Value, _request.Control?.Values[_controlIndex]);

    /// <summary>Moves the selection by <paramref name="delta"/> options, wrapping at both ends.</summary>
    public void Move(int delta)
    {
        var count = _request.Choices.Count;
        Selected = (((Selected + delta) % count) + count) % count;
    }

    /// <summary>
    /// Selects the option numbered <paramref name="number"/> (from 1) without answering: text typed ahead of a
    /// panel that just opened never answers it. A number no option carries changes nothing.
    /// </summary>
    /// <returns>True when an option carries that number.</returns>
    public bool SelectNumber(int number)
    {
        if (number < 1 || number > _request.Choices.Count) return false;
        Selected = number - 1;
        return true;
    }

    /// <summary>Selects the option <paramref name="letter"/> is the shortcut of, without answering.</summary>
    /// <returns>True when an option has that shortcut.</returns>
    public bool SelectShortcut(string letter)
    {
        for (var i = 0; i < _request.Choices.Count; i++)
        {
            if (!string.Equals(_request.Choices[i].Shortcut, letter, StringComparison.OrdinalIgnoreCase)) continue;
            Selected = i;
            return true;
        }
        return false;
    }

    /// <summary>Walks the control by <paramref name="delta"/> values, wrapping at both ends. Inert without one.</summary>
    public void AdjustControl(int delta)
    {
        if (_request.Control is not { } control || control.Values.Count == 0) return;
        var count = control.Values.Count;
        _controlIndex = (((_controlIndex + delta) % count) + count) % count;
    }

    /// <summary>
    /// The rows of the panel for <paramref name="width"/> columns and at most <paramref name="maxRows"/> rows: the
    /// list shrinks first, and when even that does not fit the panel is the options, under as much of what it asks
    /// as the rows left hold.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<RowSegment>> Rows(int width, int maxRows)
    {
        if (width <= 0 || maxRows <= 0) return [];
        var head = HeadRows(width);
        var tail = TailRows();
        var count = _request.Choices.Count;
        var visible = Math.Min(count, MaxVisibleChoices);
        var overflow = head.Count + ListHeight(visible) + tail.Count - maxRows;
        if (overflow > 0) visible = Math.Max(Math.Min(count, MinVisibleChoices), visible - overflow);

        if (head.Count + ListHeight(visible) + tail.Count > maxRows)
        {
            var fitting = ListThatFits(maxRows);
            // Past the rule, without the blank rows: the words only.
            var words = head.Skip(1).Where(row => row.Count > 0).Take(maxRows - ListHeight(fitting));
            return [.. words, .. ListRows(width, fitting)];
        }

        var rows = new List<IReadOnlyList<RowSegment>>(head);
        rows.AddRange(ListRows(width, visible));
        rows.AddRange(tail);
        return rows;
    }

    private int ListHeight(int visible) => visible + (visible < _request.Choices.Count ? 1 : 0);

    private int ListThatFits(int maxRows)
    {
        var count = _request.Choices.Count;
        if (count <= maxRows) return count;
        return Math.Max(1, maxRows - 1);
    }

    private List<IReadOnlyList<RowSegment>> HeadRows(int width)
    {
        var rows = new List<IReadOnlyList<RowSegment>>
        {
            new[] { new RowSegment(Repeat(_glyphs.RuleCell, width), PaletteRole.PanelRule) },
        };
        if (!string.IsNullOrEmpty(_request.Title))
            rows.Add([new RowSegment(_request.Title, PaletteRole.Accent, Bold: true)]);
        if (!string.IsNullOrEmpty(_request.Description))
        {
            foreach (var line in Wrap(_request.Description, width))
                rows.Add([new RowSegment(line, PaletteRole.Dim)]);
        }
        if (!string.IsNullOrEmpty(_request.Detail)) AddDetail(rows, _request.Detail, width);
        if (!string.IsNullOrEmpty(_request.Question))
        {
            AddBlank(rows);
            foreach (var line in Wrap(_request.Question, width))
                rows.Add([new RowSegment(line, PaletteRole.Normal)]);
        }
        if (rows.Count > 1) AddBlank(rows);
        return rows;
    }

    private void AddDetail(List<IReadOnlyList<RowSegment>> rows, string detail, int width)
    {
        AddBlank(rows);
        var bar = $"{_glyphs.SideBar} ";
        var lines = Wrap(detail, Math.Max(1, width - TerminalText.Columns(bar)));
        foreach (var line in lines.Take(MaxDetailRows))
            rows.Add([new RowSegment(bar, PaletteRole.Dim), new RowSegment(line, PaletteRole.Normal)]);
        if (lines.Count > MaxDetailRows)
            rows.Add([new RowSegment(bar, PaletteRole.Dim), new RowSegment($"{_glyphs.Ellipsis} +{lines.Count - MaxDetailRows}", PaletteRole.Dim)]);
    }

    private List<IReadOnlyList<RowSegment>> ListRows(int width, int visible)
    {
        var choices = _request.Choices;
        var top = Math.Clamp(Selected - (visible / 2), 0, choices.Count - visible);
        var numberColumns = choices.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        var labelColumns = LabelColumns(width, numberColumns);
        var rows = new List<IReadOnlyList<RowSegment>>(visible + 1);
        for (var i = top; i < top + visible; i++)
        {
            var scrollMark = ScrollMark(i, top, visible, choices.Count);
            rows.Add(ChoiceRow(i, numberColumns, labelColumns, scrollMark));
        }
        if (visible < choices.Count)
            rows.Add([new RowSegment($"  {_glyphs.Ellipsis} +{choices.Count - visible}", PaletteRole.Dim)]);
        return rows;
    }

    private string ScrollMark(int index, int top, int visible, int count)
    {
        if (index == top && top > 0) return _glyphs.Up;
        return index == top + visible - 1 && index < count - 1 ? _glyphs.Down : " ";
    }

    private static PaletteRole ChoiceRole(bool selected, bool isCurrent)
    {
        if (selected) return PaletteRole.Accent;
        return isCurrent ? PaletteRole.Ok : PaletteRole.Normal;
    }

    private RowSegment[] ChoiceRow(int index, int numberColumns, int labelColumns, string scrollMark)
    {
        var choice = _request.Choices[index];
        var selected = index == Selected;
        var role = ChoiceRole(selected, choice.IsCurrent);
        var number = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(numberColumns);
        var cursor = selected ? _glyphs.Prompt : scrollMark;
        var label = TerminalText.Truncate(choice.Label, labelColumns, _glyphs.Ellipsis);
        var mark = choice.IsCurrent ? $" {_glyphs.Check}" : string.Empty;
        var padding = new string(' ', Math.Max(0, labelColumns - TerminalText.Columns(label) - TerminalText.Columns(mark)));

        var row = new List<RowSegment>
        {
            new($"{cursor} {number}. ", selected ? PaletteRole.Accent : PaletteRole.Dim),
            new(label, role, Bold: selected),
        };
        if (mark.Length > 0) row.Add(new RowSegment(mark, PaletteRole.Ok));
        if (!string.IsNullOrEmpty(choice.Description))
            row.Add(new RowSegment($"{padding}  {choice.Description}", PaletteRole.Dim));
        if (selected && !string.IsNullOrEmpty(choice.Hint))
            row.Add(new RowSegment($" {_glyphs.Dot} {choice.Hint}", PaletteRole.Dim));
        return [.. row];
    }

    // The cap keeps a column for the descriptions: without any, a label has the row.
    private int LabelColumns(int width, int numberColumns)
    {
        var widest = _request.Choices.Max(c => TerminalText.Columns(c.Label) + (c.IsCurrent ? 1 + TerminalText.Columns(_glyphs.Check) : 0));
        var room = _request.Choices.Any(c => !string.IsNullOrEmpty(c.Description))
            ? Math.Min(LabelColumnCap, width / 2)
            : width - TerminalText.Columns(_glyphs.Prompt) - numberColumns - 3;
        return Math.Min(widest, Math.Max(1, room));
    }

    private List<IReadOnlyList<RowSegment>> TailRows()
    {
        var rows = new List<IReadOnlyList<RowSegment>>();
        AddBlank(rows);
        if (_request.Control is { } control && control.Values.Count > 0)
        {
            rows.Add(
            [
                new RowSegment($"{_glyphs.Bullet} ", PaletteRole.Accent),
                new RowSegment($"{control.Label} ", PaletteRole.Normal),
                new RowSegment(control.Values[_controlIndex], PaletteRole.Accent, Bold: true),
                new RowSegment($"  {_glyphs.Sideways} to adjust", PaletteRole.Dim),
            ]);
            AddBlank(rows);
        }
        rows.Add([new RowSegment(_request.Keys ?? DefaultKeys(), PaletteRole.Dim)]);
        return rows;
    }

    private string DefaultKeys()
        => _request.Cancellable ? $"Enter to confirm {_glyphs.Dot} Esc to cancel" : "Enter to confirm";

    private static void AddBlank(List<IReadOnlyList<RowSegment>> rows) => rows.Add([]);

    private static int OpeningIndex(ConsoleChoiceRequest request)
    {
        var values = request.Choices.Select(c => c.Value).ToList();
        if (request.DefaultValue is not null && IndexOf(values, request.DefaultValue) is >= 0 and var byDefault) return byDefault;
        for (var i = 0; i < request.Choices.Count; i++)
        {
            if (request.Choices[i].IsCurrent) return i;
        }
        return 0;
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    private static string Repeat(string cell, int width)
    {
        var columns = Math.Max(1, TerminalText.Columns(cell));
        return string.Concat(Enumerable.Repeat(cell, width / columns));
    }

    private static List<string> Wrap(string text, int width)
    {
        var rows = new List<string>();
        foreach (var logical in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var rest = logical;
            if (rest.Length == 0) rows.Add(string.Empty);
            while (rest.Length > 0)
            {
                var piece = TerminalText.TakeColumns(rest, width);
                if (piece.Length == 0) return rows;
                rows.Add(piece);
                rest = rest[piece.Length..];
            }
        }
        return rows;
    }
}
