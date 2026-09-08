namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>The kinds of line the fidelity transcript distinguishes.</summary>
public enum TranscriptKind
{
    /// <summary>The operator's own text — no margin glyph, exactly as the reference.</summary>
    UserTurn,

    /// <summary>First paragraph of an assistant message — bulleted.</summary>
    AssistantTurn,

    /// <summary>Follow-up paragraph of the SAME assistant message — indented, no re-bullet.</summary>
    AssistantContinuation,

    /// <summary>Aggregated tool-activity sentence (<c>Read 1 file, ran 9 shell commands</c>).</summary>
    ToolActivity,

    /// <summary>Host notification (a ticket completed) — bulleted, green in the reference.</summary>
    Notice,

    /// <summary>Recap line. Reserved: nothing emits one today — the mapping is kept so a
    /// future emitter needs no UI change.</summary>
    Recap,
}

/// <summary>
/// Pure rendering of typed transcript entries into prefixed text lines. The glyph column
/// is 2 cells (`● ` / `※ ` / nothing), continuations and activity lines indent to the text
/// column — the exact discipline of the reference captures.
/// </summary>
/// <remarks>
/// Rendering is text-only: a <c>TextView</c> paints one attribute for its whole buffer, so
/// per-glyph color (white vs green bullet) is not reachable with this widget. The structure
/// is faithful; the bullet colors are an accepted residue until the transcript becomes a
/// custom-drawn view — a known limitation rather than a silent drop.
/// </remarks>
public static class TranscriptModel
{
    /// <summary>Text-column indent used by unbulleted lines (matches the glyph column width).</summary>
    public const string Indent = "  ";

    /// <summary>
    /// Renders one entry into display lines. Multi-line text keeps the glyph on the FIRST
    /// line only; every subsequent line indents to the text column.
    /// </summary>
    public static IReadOnlyList<string> Render(GlyphSet glyphs, TranscriptKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        var lines = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var head = kind switch
        {
            TranscriptKind.AssistantTurn => $"{glyphs.Bullet} ",
            TranscriptKind.Notice => $"{glyphs.Bullet} ",
            TranscriptKind.Recap => $"{glyphs.Recap} ",
            TranscriptKind.UserTurn => string.Empty,
            _ => Indent, // continuations + tool activity indent to the text column
        };
        var continuation = head.Length == 0 ? string.Empty : Indent;

        var result = new string[lines.Length];
        for (var i = 0; i < lines.Length; i++)
            result[i] = (i == 0 ? head : continuation) + lines[i];
        return result;
    }

    /// <summary>
    /// Splits an assistant message into (first paragraph, continuations) so the bullet
    /// lands once. Paragraphs are blank-line separated, like the reference's prose.
    /// </summary>
    public static IReadOnlyList<(TranscriptKind Kind, string Text)> SplitAssistantMessage(string message)
    {
        var paragraphs = (message ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (paragraphs.Length == 0) return [];

        var result = new List<(TranscriptKind, string)>(paragraphs.Length)
        {
            (TranscriptKind.AssistantTurn, paragraphs[0]),
        };
        for (var i = 1; i < paragraphs.Length; i++)
            result.Add((TranscriptKind.AssistantContinuation, paragraphs[i]));
        return result;
    }
}
