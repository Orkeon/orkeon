namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>How the TUI decides between Unicode glyphs and their ASCII fallbacks.</summary>
public enum GlyphMode
{
    /// <summary>Unicode when the output encoding is UTF-8, ASCII otherwise (default).</summary>
    Auto,

    /// <summary>Always Unicode — for terminals whose encoding report is wrong but capable.</summary>
    Always,

    /// <summary>Always ASCII — for logs scraped by tools that choke on wide glyphs.</summary>
    Never,
}

/// <summary>
/// The fixed set of marker glyphs the fidelity layout uses, with an ASCII fallback per
/// glyph. Pure — resolution takes the encoding as an input instead of probing the console,
/// so tests cover both variants deterministically.
/// </summary>
/// <remarks>
/// The container image (`orkeon-runners`) does not guarantee a UTF-8 locale — the session
/// logs themselves show `setlocale: LC_ALL: cannot change locale`. A `●` printed through a
/// non-UTF-8 console arrives as mojibake in the exact pane an operator reads to debug, so
/// the fallback is not cosmetic.
/// </remarks>
public sealed record GlyphSet
{
    /// <summary>Prompt marker (`❯`).</summary>
    public required string Prompt { get; init; }

    /// <summary>Turn bullet — assistant paragraphs and notices (`●`).</summary>
    public required string Bullet { get; init; }

    /// <summary>Hollow bullet — non-selected agent rows (`○`).</summary>
    public required string BulletHollow { get; init; }

    /// <summary>Status-line spinner head (`✱`).</summary>
    public required string Asterisk { get; init; }

    /// <summary>Recap marker (`※`).</summary>
    public required string Recap { get; init; }

    /// <summary>Permission-posture chevrons in the hint bar (`▶▶`).</summary>
    public required string Chevrons { get; init; }

    /// <summary>Horizontal-rule cell (`─`).</summary>
    public required string RuleCell { get; init; }

    /// <summary>Down arrow used in token readouts (`↓`).</summary>
    public required string Down { get; init; }

    /// <summary>Interpunct separating hint-bar and metric segments (`·`).</summary>
    public required string Dot { get; init; }

    /// <summary>The Unicode variant — what the reference captures show.</summary>
    public static GlyphSet Unicode { get; } = new()
    {
        Prompt = "❯",
        Bullet = "●",
        BulletHollow = "○",
        Asterisk = "✱",
        Recap = "※",
        Chevrons = "▶▶",
        RuleCell = "─",
        Down = "↓",
        Dot = "·",
    };

    /// <summary>The ASCII fallback — every marker stays one column wide and 7-bit.</summary>
    public static GlyphSet Ascii { get; } = new()
    {
        Prompt = ">",
        Bullet = "*",
        BulletHollow = "o",
        Asterisk = "*",
        Recap = "#",
        Chevrons = ">>",
        RuleCell = "-",
        Down = "v",
        Dot = "-",
    };

    /// <summary>Resolves the set for a mode and an observed UTF-8 capability.</summary>
    public static GlyphSet Resolve(GlyphMode mode, bool outputIsUtf8) => mode switch
    {
        GlyphMode.Always => Unicode,
        GlyphMode.Never => Ascii,
        _ => outputIsUtf8 ? Unicode : Ascii,
    };
}
