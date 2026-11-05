using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>Pure rendering of a typed block into transcript lines.</summary>
internal static class BlockRenderer
{
    /// <summary>The lines of <paramref name="text"/>, a final line break opening none.</summary>
    public static IReadOnlyList<TranscriptLine> Render(ConsoleBlockKind kind, string text, GlyphSet glyphs, int width)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalized.EndsWith('\n')) normalized = normalized[..^1];
        if (kind == ConsoleBlockKind.Diff) return DiffRenderer.Unified(normalized);

        var markdown = new MarkdownLines(glyphs);
        var lines = new List<TranscriptLine>();
        foreach (var line in normalized.Split('\n'))
            lines.AddRange(markdown.Feed(line, width));
        lines.AddRange(markdown.Flush(width));
        return lines;
    }
}
