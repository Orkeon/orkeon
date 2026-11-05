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
        return kind == ConsoleBlockKind.Diff
            ? DiffRenderer.Unified(normalized)
            : [.. normalized.Split('\n').Select(line => new TranscriptLine(line))];
    }
}
