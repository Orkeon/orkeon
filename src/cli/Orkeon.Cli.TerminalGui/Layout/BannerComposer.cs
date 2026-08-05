using Orkeon.Cli.TerminalGui.Hosting;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Renders the startup banner as plain text lines, ready for the transcript. Pure — the
/// banner scrolls away with the conversation exactly like the reference UI's, so it is
/// transcript CONTENT, not a pinned view.
/// </summary>
/// <remarks>
/// The pixel-art block is an Orkéon glyph, deliberately. The reference captures show the
/// Claude Code logo; reproducing a third party's mark would not be fidelity, it would be
/// impersonation — the layout is copied (logo column + three text lines), the identity is
/// ours. Drawn with half-block cells so it renders in any monospace font; the ASCII glyph
/// set degrades it to a plain bracket motif.
/// </remarks>
public static class BannerComposer
{
    // 4 rows, ~7 columns — an "O" struck through, the Orkéon mark reduced to cells.
    private static readonly string[] UnicodeLogo =
    {
        "  ▄▄▄▄▄ ",
        " █  ▄  █",
        " █  ▀  █",
        "  ▀▀▀▀▀ ",
    };

    private static readonly string[] AsciiLogo =
    {
        "  .---. ",
        " |  o  |",
        " |  -  |",
        "  '---' ",
    };

    /// <summary>Column where the text block starts, matching the reference's gutter.</summary>
    private const string TextIndent = "   ";

    /// <summary>
    /// Composes the banner lines: logo column beside the (product / model / cwd) lines,
    /// then a blank line and the tip block. Empty info lines are omitted, and the logo
    /// keeps its height even when fewer text lines remain — the mark is the anchor.
    /// </summary>
    public static IReadOnlyList<string> Compose(BannerInfo info, GlyphSet glyphs)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(glyphs);

        var logo = ReferenceEquals(glyphs, GlyphSet.Ascii) || glyphs.Bullet == GlyphSet.Ascii.Bullet
            ? AsciiLogo
            : UnicodeLogo;

        var text = new List<string> { info.ProductLine };
        if (!string.IsNullOrWhiteSpace(info.ModelLine)) text.Add(info.ModelLine);
        if (!string.IsNullOrWhiteSpace(info.WorkspaceLine)) text.Add(info.WorkspaceLine);

        var rows = Math.Max(logo.Length, text.Count);
        var lines = new List<string>(rows + info.Tips.Count + 2) { string.Empty };
        for (var i = 0; i < rows; i++)
        {
            var left = i < logo.Length ? logo[i] : new string(' ', logo[0].Length);
            var right = i < text.Count ? text[i] : string.Empty;
            lines.Add($"{left}{TextIndent}{right}".TrimEnd());
        }

        if (info.Tips.Count > 0)
        {
            lines.Add(string.Empty);
            foreach (var tip in info.Tips)
                lines.Add($"  {tip}");
        }
        lines.Add(string.Empty);
        return lines;
    }
}
