using Orkeon.Cli.TerminalGui.Hosting;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Pure row formatting for the tasks list under the status bar:
/// <c>❯ ● worker@crew  checks the design document…        3m 44s · ↓ 101.1k tokens</c>.
/// Column 0 is the list cursor (accent, only while the list has focus), column 2 the bullet —
/// filled for the prompt's recipient, hollow otherwise, coloured by the task's state — then
/// the name in bold, two spaces, the activity phrase, and the metrics right-aligned and dim.
/// </summary>
public static class AgentsPaneModel
{
    /// <summary>Gap kept between the activity phrase and the right-aligned metrics.</summary>
    private const int ColumnGap = 2;

    /// <summary>Cursor column, space, bullet, space.</summary>
    private const int LeadWidth = 4;

    /// <summary>Spaces between the name and the activity phrase.</summary>
    private const string NameGap = "  ";

    /// <summary>
    /// The segments of one row, exactly <paramref name="width"/> terminal columns wide (padded or
    /// cut), measured over graphemes so an emoji or a CJK character counts its two columns. The
    /// metric block is reserved first, so on a narrow terminal the phrase gives way, then the
    /// name; only when the metrics alone overflow are they cut too, with the ellipsis.
    /// </summary>
    /// <param name="glyphs">The glyph set.</param>
    /// <param name="row">The row.</param>
    /// <param name="width">The list's width.</param>
    /// <param name="cursor">True on the selected row while the list has focus.</param>
    /// <param name="recipient">True on the row of the prompt's recipient.</param>
    internal static IReadOnlyList<RowSegment> Segments(GlyphSet glyphs, AgentRowInfo row, int width, bool cursor = false, bool recipient = false)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(row);
        if (width <= 0) return [];

        var metrics = TerminalText.Truncate(ComposeMetrics(glyphs, row), Math.Max(0, width - LeadWidth), glyphs.Ellipsis);
        var metricsColumns = TerminalText.Columns(metrics);
        var reserved = metricsColumns > 0 ? metricsColumns + ColumnGap : 0;
        var nameRoom = Math.Max(0, width - LeadWidth - reserved);
        var name = TerminalText.Truncate(row.Name, nameRoom, glyphs.Ellipsis);
        var nameColumns = TerminalText.Columns(name);
        var phraseRoom = width - LeadWidth - nameColumns - NameGap.Length - reserved;
        var phrase = phraseRoom > 0 && !string.IsNullOrWhiteSpace(row.Description)
            ? NameGap + TerminalText.Truncate(row.Description.Trim(), phraseRoom, glyphs.Ellipsis)
            : string.Empty;

        var segments = new List<RowSegment>(8)
        {
            cursor ? new RowSegment(glyphs.Prompt, PaletteRole.Accent) : new RowSegment(" ", PaletteRole.Normal),
            new(" ", PaletteRole.Normal),
            new(recipient ? glyphs.Bullet : glyphs.BulletHollow, StateRole(row.State)),
            new(" ", PaletteRole.Normal),
            new(name, PaletteRole.Normal, Bold: true),
            new(phrase, PaletteRole.Normal),
        };
        var used = LeadWidth + nameColumns + TerminalText.Columns(phrase);
        var padding = width - used - metricsColumns;
        if (padding > 0) segments.Add(new RowSegment(new string(' ', padding), PaletteRole.Normal));
        if (metrics.Length > 0) segments.Add(new RowSegment(metrics, PaletteRole.Dim));
        return Fit(segments, width);
    }

    /// <summary>The plain text of a row (see <see cref="Segments"/>).</summary>
    internal static string FormatRow(GlyphSet glyphs, AgentRowInfo row, int width, bool cursor = false, bool recipient = false)
        => RowSegment.Concat(Segments(glyphs, row, width, cursor, recipient));

    /// <summary>The bullet's role for a state: green running, red failed or cancelled, dim otherwise.</summary>
    internal static PaletteRole StateRole(TaskRowState state) => state switch
    {
        TaskRowState.Running => PaletteRole.Ok,
        TaskRowState.Failed or TaskRowState.Cancelled => PaletteRole.Error,
        _ => PaletteRole.Dim,
    };

    /// <summary>
    /// The first row a window of <paramref name="visible"/> rows shows over
    /// <paramref name="count"/> rows: the previous top, moved just enough to keep the
    /// <paramref name="cursor"/> (-1: none) in view, and clamped to the list.
    /// </summary>
    internal static int WindowTop(int count, int visible, int cursor, int previousTop)
    {
        if (visible <= 0 || count <= visible) return 0;
        var top = Math.Clamp(previousTop, 0, count - visible);
        if (cursor >= 0)
        {
            if (cursor < top) top = cursor;
            else if (cursor >= top + visible) top = cursor - visible + 1;
        }
        return Math.Clamp(top, 0, count - visible);
    }

    /// <summary>
    /// The metric block: <c>elapsed · ↓ tokens</c> for a working row, empty for a row
    /// with nothing to report (the <c>main</c> line is just <c>● main</c>). The list only
    /// ever shows LIVE work — a finished task leaves it at once and the transcript says so.
    /// Tokens are the task's total and render <c>—</c> when the host cannot attribute them.
    /// </summary>
    public static string ComposeMetrics(GlyphSet glyphs, AgentRowInfo row)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(row);

        if (row.Elapsed is null && row.Tokens is null) return string.Empty;

        var elapsed = row.Elapsed is { } e ? StatusLineFormatter.FormatElapsed(e) : "";
        var tokens = row.Tokens is { } t
            ? $"{glyphs.Down} {StatusLineFormatter.FormatTokens(t)}"
            : $"{glyphs.Down} —";
        return elapsed.Length > 0 ? $"{elapsed} {glyphs.Dot} {tokens}" : tokens;
    }

    /// <summary>
    /// The segment drawn at <paramref name="cell"/> of a row: a text view holds one grapheme per
    /// cell, so the segments are walked by grapheme count, not by UTF-16 unit. Null past the row.
    /// </summary>
    internal static RowSegment? SegmentAtCell(IReadOnlyList<RowSegment> segments, int cell)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (cell < 0) return null;
        var x = 0;
        foreach (var segment in segments)
        {
            x += TerminalText.Graphemes(segment.Text).Count;
            if (cell < x) return segment;
        }
        return null;
    }

    private static List<RowSegment> Fit(List<RowSegment> segments, int width)
    {
        var result = new List<RowSegment>(segments.Count);
        var x = 0;
        foreach (var segment in segments)
        {
            if (x >= width) break;
            if (segment.Text.Length == 0) continue;
            var text = TerminalText.TakeColumns(segment.Text, width - x);
            if (text.Length == 0) break; // a wide grapheme straddling the edge: pad instead
            result.Add(segment with { Text = text });
            x += TerminalText.Columns(text);
        }
        if (x < width) result.Add(new RowSegment(new string(' ', width - x), PaletteRole.Normal));
        return result;
    }
}
