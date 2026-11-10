using System.Globalization;
using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// The menu of commands above the prompt, without a terminal: which commands a query keeps, best match first,
/// which one is selected, and the rows it paints.
/// </summary>
internal sealed class SlashMenuModel : IPanelRows
{
    private const int MaxVisible = 8;
    private const int LabelColumnCap = 32;

    private readonly IReadOnlyList<ReplCommandEntry> _catalog;
    private readonly GlyphSet _glyphs;
    private List<ReplCommandEntry> _matches = [];

    public SlashMenuModel(IReadOnlyList<ReplCommandEntry> catalog, GlyphSet glyphs)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(glyphs);
        _catalog = catalog;
        _glyphs = glyphs;
    }

    /// <summary>How many commands the query keeps.</summary>
    public int MatchCount => _matches.Count;

    /// <summary>The index of the command selected among those kept.</summary>
    public int Selected { get; private set; }

    /// <summary>The name of the command selected; null when the query keeps none.</summary>
    public string? SelectedName => Selected < _matches.Count ? _matches[Selected].Name : null;

    /// <summary>
    /// What the menu filters on for the text of the prompt: the word after a leading <c>/</c>, or null once the text
    /// is something else (no slash, an argument begun, a second line), which is where the menu has nothing to say.
    /// </summary>
    public static string? QueryOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.StartsWith('/')) return null;
        var query = text[1..];
        return query.Any(char.IsWhiteSpace) ? null : query;
    }

    /// <summary>Keeps the commands <paramref name="query"/> names and selects the best one.</summary>
    public void Filter(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        _matches =
        [
            .. _catalog
                .Select(entry => (Entry: entry, Rank: RankOf(entry, query)))
                .Where(match => match.Rank >= 0)
                .OrderBy(match => match.Rank)
                .ThenBy(match => match.Entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(match => match.Entry),
        ];
        Selected = 0;
    }

    /// <summary>Moves the selection by <paramref name="delta"/> commands, wrapping at both ends.</summary>
    public void Move(int delta)
    {
        if (_matches.Count == 0) return;
        Selected = (((Selected + delta) % _matches.Count) + _matches.Count) % _matches.Count;
    }

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<RowSegment>> Rows(int width, int maxRows)
    {
        if (width <= 0 || maxRows <= 0 || _matches.Count == 0) return [];
        var visible = Math.Min(_matches.Count, Math.Min(MaxVisible, maxRows));
        var top = Math.Clamp(Selected - (visible / 2), 0, _matches.Count - visible);
        var labelColumns = LabelColumns(top, visible, width);
        var rows = new List<IReadOnlyList<RowSegment>>(visible);
        for (var i = top; i < top + visible; i++)
        {
            var mark = ScrollMark(i, top, visible);
            rows.Add(Row(i, labelColumns, mark, width));
        }
        return rows;
    }

    private string ScrollMark(int index, int top, int visible)
    {
        if (index == top && top > 0) return _glyphs.Up;
        return index == top + visible - 1 && index < _matches.Count - 1 ? _glyphs.Down : " ";
    }

    // 0 a name that starts with the query, 1 an alias that does, 2 a name that holds it; -1 nothing.
    private static int RankOf(ReplCommandEntry entry, string query)
    {
        if (entry.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (entry.Aliases.Any(alias => alias.StartsWith(query, StringComparison.OrdinalIgnoreCase))) return 1;
        return entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : -1;
    }

    private RowSegment[] Row(int index, int labelColumns, string mark, int width)
    {
        var entry = _matches[index];
        var selected = index == Selected;
        var cursor = selected ? _glyphs.Prompt : mark;
        var label = TerminalText.Truncate(Label(entry), labelColumns, _glyphs.Ellipsis);
        var padding = new string(' ', Math.Max(0, labelColumns - TerminalText.Columns(label)));
        var head = string.Create(CultureInfo.InvariantCulture, $"{cursor} {label}{padding}  ");
        var room = Math.Max(0, width - TerminalText.Columns(head));
        var description = TerminalText.Truncate(FirstLine(entry.Description), room, _glyphs.Ellipsis);
        return
        [
            new RowSegment(head, selected ? PaletteRole.Accent : PaletteRole.Normal, Bold: selected),
            new RowSegment(description, selected ? PaletteRole.Accent : PaletteRole.Dim),
        ];
    }

    private int LabelColumns(int top, int visible, int width)
    {
        var widest = _matches.Skip(top).Take(visible).Max(entry => TerminalText.Columns(Label(entry)));
        return Math.Clamp(widest, 1, Math.Max(1, Math.Min(LabelColumnCap, width / 2)));
    }

    private static string Label(ReplCommandEntry entry)
        => entry.Aliases.Count == 0 ? $"/{entry.Name}" : $"/{entry.Name} ({string.Join(", ", entry.Aliases)})";

    private static string FirstLine(string text)
    {
        var end = text.IndexOf('\n', StringComparison.Ordinal);
        return (end < 0 ? text : text[..end]).TrimEnd();
    }
}
