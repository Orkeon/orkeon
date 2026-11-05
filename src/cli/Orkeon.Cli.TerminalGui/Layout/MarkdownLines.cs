using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Reads markdown one finished line at a time, as a stream delivers it, and answers the lines ready to show. A table
/// is held until it ends, because its columns depend on all its rows. Past that, each line stands alone: an emphasis
/// open across two lines stays as written. A copy of a whole line gives back its source.
/// </summary>
internal sealed partial class MarkdownLines(GlyphSet glyphs)
{
    private const int MaxTableRows = 200;
    private const int MaxRuleColumns = 40;
    private const string CodeIndent = "  ";

    // The box characters of GlyphSet.Box, in this order.
    private const int TopLeft = 0, TopMid = 1, TopRight = 2, MidLeft = 3, MidMid = 4, MidRight = 5, BottomLeft = 6, BottomMid = 7, BottomRight = 8, Horizontal = 9, Vertical = 10;

    private string? _header;
    private PendingTable? _table;
    private Fence? _fence;
    private string? _fenceOpening;

    private sealed record PendingTable(string Header, string Delimiter)
    {
        public List<string> Rows { get; } = [];
    }

    private sealed record Fence(char Marker, int Length);

    private enum Align
    {
        Left,
        Center,
        Right,
    }

    /// <summary>Whether lines are held back, waiting for what comes next.</summary>
    public bool Holding => _header is not null || _table is not null;

    /// <summary>What <paramref name="line"/> makes ready to show.</summary>
    /// <param name="line">A finished line, with no line break.</param>
    /// <param name="width">The columns available; zero or less when unknown.</param>
    public IReadOnlyList<TranscriptLine> Feed(string line, int width)
    {
        ArgumentNullException.ThrowIfNull(line);
        var output = new List<TranscriptLine>();
        if (_fence is { } fence)
        {
            InFence(line, fence, output);
            return output;
        }

        if (_table is { } table)
        {
            if (line.Contains('|', StringComparison.Ordinal) && table.Rows.Count < MaxTableRows)
            {
                table.Rows.Add(line);
                return output;
            }
            output.AddRange(RenderTable(table, width));
            _table = null;
        }

        if (_header is { } header)
        {
            _header = null;
            if (IsDelimiter(line))
            {
                _table = new PendingTable(header, line);
                return output;
            }
            output.Add(Block(header, width));
        }

        var opening = FenceLine().Match(line);
        if (opening.Success)
        {
            OpenFence(line, opening, output);
        }
        else if (line.Contains('|', StringComparison.Ordinal))
        {
            _header = line;
        }
        else
        {
            output.Add(Block(line, width));
        }
        return output;
    }

    /// <summary>What was held back, shown as it stands. A code fence stays open for the lines to come.</summary>
    public IReadOnlyList<TranscriptLine> Flush(int width)
    {
        var output = new List<TranscriptLine>();
        if (_table is { } table) output.AddRange(RenderTable(table, width));
        if (_header is { } header) output.Add(Block(header, width));
        _table = null;
        _header = null;
        return output;
    }

    private void OpenFence(string line, Match opening, List<TranscriptLine> output)
    {
        _fence = new Fence(opening.Groups[1].Value[0], opening.Groups[1].Length);
        var language = opening.Groups[2].Value.Trim();
        if (language.Length == 0)
            _fenceOpening = line;
        else
            output.Add(TranscriptLine.InRole(language, PaletteRole.Dim) with { CopySource = line });
    }

    private void InFence(string line, Fence fence, List<TranscriptLine> output)
    {
        var closing = FenceLine().Match(line);
        if (closing.Success && closing.Groups[1].Value[0] == fence.Marker
            && closing.Groups[1].Length >= fence.Length && closing.Groups[2].Value.Trim().Length == 0)
        {
            _fence = null;
            _fenceOpening = null;
            output.Add(new TranscriptLine(string.Empty) { CopySource = line });
            return;
        }

        var source = _fenceOpening is null ? line : _fenceOpening + "\n" + line;
        _fenceOpening = null;
        var shown = CodeIndent + line.Replace("\t", "    ", StringComparison.Ordinal);
        output.Add(TranscriptLine.InRole(shown, PaletteRole.Code) with { CopySource = source, HangingIndent = CodeIndent.Length });
    }

    private TranscriptLine Block(string line, int width)
    {
        if (string.IsNullOrWhiteSpace(line)) return new TranscriptLine(string.Empty);

        if (Rule().IsMatch(line))
        {
            var columns = width > 0 ? Math.Min(width, MaxRuleColumns) : MaxRuleColumns;
            var rule = string.Concat(Enumerable.Repeat(glyphs.RuleCell, columns));
            return TranscriptLine.InRole(rule, PaletteRole.PanelRule) with { CopySource = line };
        }

        var heading = Heading().Match(line);
        if (heading.Success)
            return Styled(string.Empty, heading.Groups[1].Value, line, bold: true);

        var quote = Quote().Match(line);
        if (quote.Success)
        {
            var bar = glyphs.SideBar + " ";
            var (text, runs) = MarkdownInline.Parse(quote.Groups[1].Value, italic: true, role: PaletteRole.Dim);
            return new TranscriptLine(bar + text)
            {
                Runs = [new TranscriptRun(0, bar.Length, PaletteRole.PanelRule), .. Shifted(runs, bar.Length)],
                HangingIndent = bar.Length,
                CopySource = line,
            };
        }

        var item = ListItem().Match(line);
        if (item.Success)
        {
            var marker = char.IsDigit(item.Groups[2].Value[0]) ? item.Groups[2].Value : "-";
            return Styled($"{item.Groups[1].Value}{marker} ", item.Groups[3].Value, line, bold: false);
        }

        var indent = line[..^line.TrimStart().Length];
        return Styled(indent, line.TrimStart(), line, bold: false);
    }

    private static TranscriptLine Styled(string head, string content, string source, bool bold)
    {
        var (text, runs) = MarkdownInline.Parse(content, bold: bold);
        var shown = head + text;
        return new TranscriptLine(shown)
        {
            Runs = Shifted(runs, head.Length),
            HangingIndent = TerminalText.Columns(head),
            CopySource = shown == source ? null : source,
        };
    }

    private static List<TranscriptRun> Shifted(IReadOnlyList<TranscriptRun> runs, int by)
        => [.. runs.Select(r => r with { Start = r.Start + by })];

    private static bool IsDelimiter(string line)
    {
        if (!line.Contains('-', StringComparison.Ordinal)) return false;
        var cells = Cells(line);
        return cells.Count > 0 && cells.TrueForAll(c => DelimiterCell().IsMatch(c));
    }

    // The cells of a row: a bar that is escaped or inside a code span does not cut one.
    private static List<string> Cells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inCode = false;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c == '\\' && i + 1 < trimmed.Length && trimmed[i + 1] == '|')
            {
                cell.Append('|');
                i++;
            }
            else if (c == '|' && !inCode)
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else
            {
                if (c == '`') inCode = !inCode;
                cell.Append(c);
            }
        }
        if (cell.ToString().Trim().Length > 0) cells.Add(cell.ToString().Trim());
        return cells;
    }

    private List<TranscriptLine> RenderTable(PendingTable table, int width)
    {
        var aligns = Cells(table.Delimiter).ConvertAll(AlignOf);
        var header = Cells(table.Header).ConvertAll(c => MarkdownInline.Parse(c, bold: true));
        var body = table.Rows.ConvertAll(r => Cells(r).ConvertAll(c => MarkdownInline.Parse(c)));
        var count = Math.Max(header.Count, body.Count == 0 ? 0 : body.Max(r => r.Count));
        var widths = new int[count];
        foreach (var row in body.Prepend(header))
        {
            for (var i = 0; i < row.Count; i++)
                widths[i] = Math.Max(widths[i], TerminalText.Columns(row[i].Text));
        }

        var total = widths.Sum() + (3 * count) + 1;
        if (width > 0 && total > width) return Stacked(header, body);

        var lines = new List<TranscriptLine> { Border(widths, TopLeft, TopMid, TopRight, string.Empty) };
        lines.Add(TableRow(header, widths, aligns, table.Header));
        lines.Add(Border(widths, MidLeft, MidMid, MidRight, table.Delimiter));
        for (var i = 0; i < body.Count; i++)
            lines.Add(TableRow(body[i], widths, aligns, table.Rows[i]));
        lines.Add(Border(widths, BottomLeft, BottomMid, BottomRight, string.Empty));
        return lines;
    }

    private static Align AlignOf(string delimiter)
    {
        var left = delimiter.StartsWith(':');
        var right = delimiter.EndsWith(':');
        if (left && right) return Align.Center;
        return right ? Align.Right : Align.Left;
    }

    private TranscriptLine Border(int[] widths, int left, int middle, int right, string source)
    {
        var horizontal = glyphs.Box[Horizontal];
        var cells = widths.Select(w => new string(horizontal, w + 2));
        var text = glyphs.Box[left] + string.Join(glyphs.Box[middle], cells) + glyphs.Box[right];
        return TranscriptLine.InRole(text, PaletteRole.PanelRule) with { CopySource = source };
    }

    private TranscriptLine TableRow(List<(string Text, IReadOnlyList<TranscriptRun> Runs)> row, int[] widths, List<Align> aligns, string source)
    {
        var text = new StringBuilder();
        var runs = new List<TranscriptRun>();
        for (var i = 0; i < widths.Length; i++)
        {
            var (cell, cellRuns) = i < row.Count ? row[i] : (string.Empty, []);
            var space = widths[i] - TerminalText.Columns(cell);
            var before = (i < aligns.Count ? aligns[i] : Align.Left) switch
            {
                Align.Right => space,
                Align.Center => space / 2,
                _ => 0,
            };
            runs.Add(new TranscriptRun(text.Length, 2, PaletteRole.PanelRule));
            text.Append(glyphs.Box[Vertical]).Append(' ').Append(' ', before);
            runs.AddRange(Shifted(cellRuns, text.Length));
            text.Append(cell).Append(' ', space - before + 1);
        }
        runs.Add(new TranscriptRun(text.Length, 1, PaletteRole.PanelRule));
        text.Append(glyphs.Box[Vertical]);
        return new TranscriptLine(text.ToString()) { Runs = runs, CopySource = source };
    }

    // Too wide for the screen: each row reads as its own short list, nothing cut.
    private static List<TranscriptLine> Stacked(
        List<(string Text, IReadOnlyList<TranscriptRun> Runs)> header,
        List<List<(string Text, IReadOnlyList<TranscriptRun> Runs)>> body)
    {
        var lines = new List<TranscriptLine>();
        for (var r = 0; r < body.Count; r++)
        {
            if (r > 0) lines.Add(new TranscriptLine(string.Empty));
            for (var c = 0; c < body[r].Count; c++)
            {
                var name = c < header.Count ? header[c].Text : (c + 1).ToString(CultureInfo.InvariantCulture);
                var head = name + ": ";
                lines.Add(new TranscriptLine(head + body[r][c].Text)
                {
                    Runs = [new TranscriptRun(0, name.Length, PaletteRole.Normal, Bold: true), .. Shifted(body[r][c].Runs, head.Length)],
                    HangingIndent = 2,
                });
            }
        }
        return lines;
    }

    [GeneratedRegex(@"^\s{0,3}(`{3,}|~{3,})(.*)$")]
    private static partial Regex FenceLine();

    [GeneratedRegex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+(.*?)\s*#*\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s{0,3}>\s?(.*)$")]
    private static partial Regex Quote();

    [GeneratedRegex(@"^(\s*)([-*+]|\d{1,9}[.)])\s+(.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^:?-+:?$")]
    private static partial Regex DelimiterCell();
}
