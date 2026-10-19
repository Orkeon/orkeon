using Terminal.Gui.ViewBase;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// One run of text on a row of the bottom area, with the semantic role (and emphasis) it is
/// painted in. The pure models compose rows as lists of segments; the views only paint them.
/// </summary>
internal readonly record struct RowSegment(string Text, PaletteRole Role, bool Bold = false, bool Italic = false)
{
    /// <summary>The plain text of a row: what a test reads and what a copy returns.</summary>
    public static string Concat(IEnumerable<RowSegment> segments)
        => string.Concat(segments.Select(s => s.Text));
}

/// <summary>Paints a row of <see cref="RowSegment"/>s into a view, each in its own attribute.</summary>
internal static class SegmentPainter
{
    /// <summary>
    /// Clears row <paramref name="y"/> of <paramref name="view"/>'s viewport to the terminal's
    /// background, then writes the segments from column 0.
    /// </summary>
    public static void Paint(View view, int y, IReadOnlyList<RowSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(segments);
        var width = view.Viewport.Width;
        if (width <= 0) return;

        view.SetAttribute(SchemeFactory.Attr(PaletteRole.Normal));
        view.Move(0, y);
        view.AddStr(new string(' ', width));

        var x = 0;
        foreach (var segment in segments)
        {
            if (x >= width) break;
            var text = segment.Text.Length > width - x ? segment.Text[..(width - x)] : segment.Text;
            if (text.Length == 0) continue;
            view.SetAttribute(SchemeFactory.Attr(segment.Role, segment.Bold, segment.Italic));
            view.Move(x, y);
            view.AddStr(text);
            x += text.Length;
        }
    }
}
