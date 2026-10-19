using Terminal.Gui.Drawing;
using Terminal.Gui.Text;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Terminal measures of a string: its grapheme clusters (one text-view cell each) and the
/// columns they take on screen (two for an emoji or a CJK character), through the helpers
/// Terminal.Gui draws with, so a row measured here lines up with what the driver renders.
/// </summary>
internal static class TerminalText
{
    /// <summary>The grapheme clusters of <paramref name="text"/>, in order.</summary>
    public static IReadOnlyList<string> Graphemes(string? text)
        => string.IsNullOrEmpty(text) ? [] : GraphemeHelper.GetGraphemes(text).ToList();

    /// <summary>The columns <paramref name="text"/> takes on screen.</summary>
    public static int Columns(string? text)
    {
        var columns = 0;
        foreach (var grapheme in Graphemes(text)) columns += Width(grapheme);
        return columns;
    }

    /// <summary>
    /// The longest prefix of <paramref name="text"/> that fits in <paramref name="max"/> columns,
    /// cut between graphemes (a wide grapheme that would straddle the limit is left out).
    /// </summary>
    public static string TakeColumns(string? text, int max)
    {
        if (string.IsNullOrEmpty(text) || max <= 0) return string.Empty;
        var builder = new System.Text.StringBuilder(text.Length);
        var columns = 0;
        foreach (var grapheme in Graphemes(text))
        {
            var width = Width(grapheme);
            if (columns + width > max) break;
            builder.Append(grapheme);
            columns += width;
        }
        return builder.ToString();
    }

    /// <summary>
    /// <paramref name="text"/> cut to <paramref name="max"/> columns, ending with
    /// <paramref name="ellipsis"/> when it was cut (the ellipsis alone, itself cut, when the room
    /// is narrower than it).
    /// </summary>
    public static string Truncate(string? text, int max, string ellipsis)
    {
        text ??= string.Empty;
        if (max <= 0) return string.Empty;
        if (Columns(text) <= max) return text;
        var room = max - Columns(ellipsis);
        return room <= 0 ? TakeColumns(ellipsis, max) : TakeColumns(text, room) + ellipsis;
    }

    private static int Width(string grapheme) => Math.Max(0, grapheme.GetColumns());
}
