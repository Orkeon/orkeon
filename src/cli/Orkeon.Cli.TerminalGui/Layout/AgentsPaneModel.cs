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
    /// </summary>
    public static string FormatRow(GlyphSet glyphs, AgentRowInfo row, int width, int nameColumn)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(row);
        if (width <= 0) return string.Empty;

        var bullet = row.IsActive ? glyphs.Bullet : glyphs.BulletHollow;
        var metrics = ComposeMetrics(glyphs, row);

        var name = row.Name.Length > nameColumn ? Truncate(row.Name, nameColumn) : row.Name.PadRight(nameColumn);
        var left = $" {bullet} {name}  ";

        var room = width - left.Length - metrics.Length - ColumnGap;
        var description = room > 0 ? Truncate(row.Description, room).PadRight(room) : string.Empty;

        var line = left + description + new string(' ', Math.Max(0, ColumnGap)) + metrics;
        return line.Length > width ? line[..width] : line.PadRight(width);
    }

    /// <summary>`idle` for a terminal row; `elapsed · ↓ tokens` otherwise, tokens `—` when unattributable.</summary>
    public static string ComposeMetrics(GlyphSet glyphs, AgentRowInfo row)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(row);
        if (row.IsIdle) return "idle";

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
