using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>Pure rendering of a typed block into transcript lines.</summary>
internal static class BlockRenderer
{
    /// <summary>The lines of <paramref name="text"/>, a final line break opening none.</summary>
    public static IReadOnlyList<TranscriptLine> Render(ConsoleBlockKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalized.EndsWith('\n')) normalized = normalized[..^1];
        var lines = normalized.Split('\n');
        return kind == ConsoleBlockKind.Diff
            ? [.. lines.Select(DiffLine)]
            : [.. lines.Select(line => new TranscriptLine(line))];
    }

    private static TranscriptLine DiffLine(string line)
    {
        if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal)
            || line.StartsWith("diff ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal))
            return TranscriptLine.InRole(line, PaletteRole.Dim);
        if (line.StartsWith("@@", StringComparison.Ordinal)) return TranscriptLine.InRole(line, PaletteRole.Code);
        if (line.StartsWith('+')) return new TranscriptLine(line) { RowRole = PaletteRole.DiffAdded };
        if (line.StartsWith('-')) return new TranscriptLine(line) { RowRole = PaletteRole.DiffRemoved };
        return new TranscriptLine(line);
    }
}
