using Orkeon.Cli.TerminalGui.Hosting;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Pure row formatting for the agents pane (PLAN C9):
/// <c> ● name  description…                    3m 44s · ↓ 101.1k tokens</c>, with `idle`
/// replacing the metric pair on a terminal row and hard right-alignment of the metrics.
/// </summary>
public static class AgentsPaneModel
{
    /// <summary>Gap kept between the truncated description and the right-aligned metrics.</summary>
    private const int ColumnGap = 2;

    /// <summary>
    /// Formats one row into exactly <paramref name="width"/> columns (padded / truncated).
    /// The metric block is computed first so the description gets whatever room remains —
    /// on a narrow terminal it is the prose that gives way, never the numbers.
    /// <paramref name="selected"/> swaps the leading space for the prompt chevron (the
    /// pane's F4 selection cursor).
    /// </summary>
    public static string FormatRow(GlyphSet glyphs, AgentRowInfo row, int width, int nameColumn, bool selected = false)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(row);
        if (width <= 0) return string.Empty;

        var bullet = row.IsActive ? glyphs.Bullet : glyphs.BulletHollow;
        var metrics = ComposeMetrics(glyphs, row);

        var name = row.Name.Length > nameColumn ? Truncate(row.Name, nameColumn) : row.Name.PadRight(nameColumn);
        var left = $"{(selected ? glyphs.Prompt : " ")} {bullet} {name}  ";

        var room = width - left.Length - metrics.Length - ColumnGap;
        var description = room > 0 ? Truncate(row.Description, room).PadRight(room) : string.Empty;

        var line = left + description + new string(' ', Math.Max(0, ColumnGap)) + metrics;
        return line.Length > width ? line[..width] : line.PadRight(width);
    }

    /// <summary>
    /// The metric block: <c>elapsed · ↓ tokens</c> for a working row, empty for a row
    /// with nothing to report (the reference's <c>main</c> line is just <c>● main</c>).
    /// The pane only ever shows LIVE work — finished agents leave the pane immediately
    /// (an earlier iteration rendered them as <c>✓ done</c>/<c>idle</c> during a
    /// retention window; the user ruled that out: <c>idle</c> means *waiting*, not
    /// *finished*, and a finished agent simply disappears). Tokens render <c>—</c> when
    /// the host cannot attribute them.
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

    /// <summary>Widest name across the rows, so the name column aligns like the reference.</summary>
    public static int NameColumn(IReadOnlyList<AgentRowInfo> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var widest = 0;
        foreach (var r in rows) widest = Math.Max(widest, r.Name.Length);
        return Math.Min(Math.Max(widest, 4), 24); // floor for "main", cap so prose keeps room
    }

    private static string Truncate(string text, int max)
    {
        text ??= string.Empty;
        if (max <= 0) return string.Empty;
        if (text.Length <= max) return text;
        return max == 1 ? "…" : text[..(max - 1)] + "…";
    }
}
