namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Pure composition of the separators framing the prompt: a dim rule across the full width
/// and, on the top one while a task other than <c>main</c> is the recipient,
/// <c>── &lt;name&gt; ─</c> embedded at its right end in the accent.
/// </summary>
internal static class SeparatorModel
{
    /// <summary>Cells the label adds around the name: two rule cells and a space before, a space and one cell after.</summary>
    private const int LabelChrome = 5;

    /// <summary>The separator's segments, exactly <paramref name="width"/> columns wide.</summary>
    public static IReadOnlyList<RowSegment> Compose(GlyphSet glyphs, int width, string? label)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        if (width <= 0) return [];
        var cell = glyphs.RuleCell.Length > 0 ? glyphs.RuleCell[..1] : "-";
        var name = (label ?? string.Empty).Trim();
        var room = width - LabelChrome;
        if (name.Length == 0 || room < 1)
            return [new RowSegment(Repeat(cell, width), PaletteRole.Dim)];

        if (name.Length > room) name = room == 1 ? "…" : string.Concat(name.AsSpan(0, room - 1), "…");
        var embedded = $"{cell}{cell} {name} {cell}";
        return
        [
            new RowSegment(Repeat(cell, width - embedded.Length), PaletteRole.Dim),
            new RowSegment(embedded, PaletteRole.Accent),
        ];
    }

    private static string Repeat(string cell, int count)
        => count <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(cell, count));
}
