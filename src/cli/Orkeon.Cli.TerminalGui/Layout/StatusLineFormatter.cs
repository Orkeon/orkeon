namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>What the in-flight turn is doing right now, as far as the host can tell.</summary>
public enum TurnState
{
    /// <summary>Default: the command runs, no finer signal.</summary>
    Working,

    /// <summary>LLM content deltas are arriving (the streaming sink is active).</summary>
    Streaming,

    /// <summary>A tool call is in flight (an open <c>tool.call</c> span).</summary>
    Tool,
}

/// <summary>
/// Pure formatting for the status line: <c>✱ Reasoning… (3m 44s · ↓ 118.3k tokens · streaming)</c>.
/// The view owns the timer and the sources; every formatting decision is here so it can be
/// pinned by tests without loading a Terminal.Gui type.
/// </summary>
public static class StatusLineFormatter
{
    /// <summary>
    /// The gerund rotation. Orkéon's own words, deliberately — the reference UI's
    /// ("Frolicking", "Booping") are its tone, not ours. Sober but not lifeless.
    /// One is picked per turn and stays stable for that turn (index by turn seed).
    /// </summary>
    public static readonly IReadOnlyList<string> Gerunds =
    [
        "Working", "Reasoning", "Exploring", "Composing", "Weaving", "Orchestrating",
    ];

    /// <summary>Stable gerund for a turn: seeded by the turn's start tick, not by wall time.</summary>
    public static string GerundFor(long turnSeed)
        => Gerunds[(int)((ulong)turnSeed % (ulong)Gerunds.Count)];

    /// <summary>
    /// <c>8s</c> under a minute, <c>3m 44s</c> under an hour, <c>1h 02m</c> beyond —
    /// the reference's own three formats.
    /// </summary>
    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        if (elapsed.TotalHours >= 1) return $"{(int)elapsed.TotalHours}h {elapsed.Minutes:D2}m";
        if (elapsed.TotalMinutes >= 1) return $"{elapsed.Minutes}m {elapsed.Seconds:D2}s";
        return $"{elapsed.Seconds}s";
    }

    /// <summary>
    /// <c>126 tokens</c> below 1000, <c>118.3k tokens</c> above — one decimal, floor
    /// (the reference truncates rather than rounds up: 118 399 reads 118.3k).
    /// </summary>
    public static string FormatTokens(long tokens)
    {
        if (tokens < 0) tokens = 0;
        if (tokens < 1000) return $"{tokens} tokens";
        var k = Math.Floor(tokens / 100d) / 10d;
        return $"{k.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}k tokens";
    }

    /// <summary>
    /// Assembles the full line. Absent parts are OMITTED, never placeholdered: the
    /// short form <c>✱ Working… (8s)</c> is what the reference shows when it has
    /// nothing more to say — an invented `0 tokens` would read as a measurement.
    /// </summary>
    public static string Compose(
        GlyphSet glyphs,
        string gerund,
        TimeSpan elapsed,
        long? tokens = null,
        TurnState? state = null)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        var parts = new List<string>(3) { FormatElapsed(elapsed) };
        if (tokens is { } t) parts.Add($"{glyphs.Down} {FormatTokens(t)}");
        if (state is { } s) parts.Add(DescribeState(s));
        return $"{glyphs.Asterisk} {gerund}… ({string.Join($" {glyphs.Dot} ", parts)})";
    }

    private static string DescribeState(TurnState state) => state switch
    {
        TurnState.Streaming => "streaming",
        TurnState.Tool => "tool",
        _ => "working",
    };
}
