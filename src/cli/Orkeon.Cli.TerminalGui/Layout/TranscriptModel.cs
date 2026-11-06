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
/// <see cref="Render"/> gives text lines; the entries with a display of their own
/// (<see cref="UserPrompt"/>, <see cref="TurnEnd"/>) give <see cref="TranscriptLine"/>s.
/// </remarks>
public static class TranscriptModel
{
    /// <summary>Text-column indent used by unbulleted lines (matches the glyph column width).</summary>
    public const string Indent = "  ";

    /// <summary>A turn shorter than this gets no closing line: it is over before the reader looks up.</summary>
    internal static readonly TimeSpan TurnEndThreshold = TimeSpan.FromSeconds(10);

    /// <summary>
    /// What the operator submitted, each logical line on a grey bar: a blank row to set it off from what
    /// precedes it, the prompt marker on the first line, the text aligned under it when it wraps. A copy of
    /// a whole line returns what was typed, without the marker.
    /// </summary>
    internal static IReadOnlyList<TranscriptLine> UserPrompt(GlyphSet glyphs, string text)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        var marker = $"{glyphs.Prompt} ";
        var indent = new string(' ', marker.Length);
        var lines = new List<TranscriptLine> { new(string.Empty) };
        var logical = (text ?? string.Empty).Split('\n');
        for (var i = 0; i < logical.Length; i++)
        {
            lines.Add(new TranscriptLine((i == 0 ? marker : indent) + logical[i])
            {
                RowRole = PaletteRole.UserBar,
                HangingIndent = marker.Length,
                CopySource = logical[i],
            });
        }
        return lines;
    }

    /// <summary>
    /// The dim line that closes a long turn, <c>  Done in 3m 44s · ↓ 118.3k tokens</c>; null for a short one.
    /// Absent tokens are omitted, as on the status line.
    /// </summary>
    internal static TranscriptLine? TurnEnd(GlyphSet glyphs, TimeSpan elapsed, long? tokens)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        if (elapsed < TurnEndThreshold) return null;
        var text = $"{Indent}Done in {StatusLineFormatter.FormatElapsed(elapsed)}";
        if (tokens is > 0) text += $" {glyphs.Dot} {glyphs.Down} {StatusLineFormatter.FormatTokens(tokens.Value)}";
        return TranscriptLine.InRole(text, PaletteRole.Dim);
    }

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
    /// The line an Esc Esc interrupt leaves under the interrupted turn:
    /// <c>  ⎿  Interrupted · What should Orkeon do instead?</c>.
    /// </summary>
    internal static string Interrupted(GlyphSet glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        return $"{Indent}{glyphs.Elbow}  Interrupted {glyphs.Dot} What should Orkeon do instead?";
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
