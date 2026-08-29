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
    /// The default gerund rotation. Orkeon's own words, deliberately — the reference UI's
    /// ("Frolicking", "Booping") are its tone, not ours. Sober but not lifeless.
    /// Overridable per session via the <c>spinnerVerbs</c> setting (tweakcc vocabulary:
    /// "thinking verbs"), which flows in through <c>TuiIntegration.SpinnerVerbs</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> Gerunds =
    [
        "Working", "Reasoning", "Exploring", "Composing", "Weaving", "Orchestrating",
    ];

    /// <summary>How long one spinner verb stays up before the rotation re-draws.</summary>
    private static readonly TimeSpan VerbRotationPeriod = TimeSpan.FromSeconds(15);

    /// <summary>How long one spinner frame stays up.</summary>
    private static readonly TimeSpan FramePeriod = TimeSpan.FromMilliseconds(250);

    /// <summary>Stable gerund for a turn: seeded by the turn's start tick, not by wall time.</summary>
    public static string GerundFor(long turnSeed)
        => Gerunds[(int)((ulong)turnSeed % (ulong)Gerunds.Count)];

    /// <summary>
    /// Spinner verb for a turn at a given elapsed time: stable within a rotation period,
    /// advancing every <see cref="VerbRotationPeriod"/> so a long turn stays alive. A null
    /// or empty <paramref name="verbs"/> falls back to <see cref="Gerunds"/>; blank
    /// entries are skipped at selection.
    /// </summary>
    public static string VerbFor(long turnSeed, TimeSpan elapsed, IReadOnlyList<string>? verbs)
    {
        var pool = verbs is { Count: > 0 } v ? v : Gerunds;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var rotation = (ulong)(elapsed.Ticks / VerbRotationPeriod.Ticks);
        for (var attempt = 0; attempt < pool.Count; attempt++)
        {
            var candidate = pool[(int)(((ulong)turnSeed + rotation + (ulong)attempt) % (ulong)pool.Count)];
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate.Trim();
        }
        return Gerunds[0];
    }

    /// <summary>The spinner frame for a given elapsed time — one step per quarter second.</summary>
    public static string SpinnerFrame(GlyphSet glyphs, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        var frames = glyphs.SpinnerFrames;
        if (frames.Count == 0) return glyphs.Asterisk;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        return frames[(int)((ulong)(elapsed.Ticks / FramePeriod.Ticks) % (ulong)frames.Count)];
    }

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

    /// <summary>Cells in the progress bar — matches the reference capture's width class.</summary>
    private const int BarCells = 10;

    /// <summary>
    /// Assembles the full line. Absent parts are OMITTED, never placeholdered: the
    /// short form <c>✱ Working… (8s)</c> is what the reference shows when it has
    /// nothing more to say — an invented `0 tokens` would read as a measurement.
    /// <paramref name="head"/> lets the view animate the leading glyph; null keeps
    /// the static asterisk.
    /// </summary>
    public static string Compose(
        GlyphSet glyphs,
        string gerund,
        TimeSpan elapsed,
        long? tokens = null,
        TurnState? state = null,
        string? head = null)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        var parts = new List<string>(3) { FormatElapsed(elapsed) };
        if (tokens is { } t) parts.Add($"{glyphs.Down} {FormatTokens(t)}");
        if (state is { } s) parts.Add(DescribeState(s));
        return $"{head ?? glyphs.Asterisk} {gerund}… ({string.Join($" {glyphs.Dot} ", parts)})";
    }

    /// <summary>
    /// The progress form of the line:
    /// <c>✳ Compacting conversation… ▰▰▰▱▱▱▱▱▱▱ 34% (12s)</c>, or — when the operation
    /// cannot measure a ratio — <c>✳ Compacting conversation… (12s · extracting memories)</c>.
    /// The percent floors (34.9 reads 34): a bar that overstates progress reads as stalled
    /// at the end, understating never does.
    /// </summary>
    public static string ComposeProgress(
        GlyphSet glyphs,
        string label,
        double? ratio,
        string? message,
        TimeSpan elapsed,
        string? head = null)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        var lead = $"{head ?? glyphs.Asterisk} {label}…";

        if (ratio is { } r)
        {
            r = Math.Clamp(r, 0d, 1d);
            var filled = (int)Math.Floor(r * BarCells);
            var bar = string.Concat(Enumerable.Repeat(glyphs.BarFilled, filled))
                    + string.Concat(Enumerable.Repeat(glyphs.BarEmpty, BarCells - filled));
            var percent = (int)Math.Floor(r * 100d);
            return $"{lead} {bar} {percent}% ({FormatElapsed(elapsed)})";
        }

        var parts = new List<string>(2) { FormatElapsed(elapsed) };
        if (!string.IsNullOrWhiteSpace(message)) parts.Add(message.Trim());
        return $"{lead} ({string.Join($" {glyphs.Dot} ", parts)})";
    }

    private static string DescribeState(TurnState state) => state switch
    {
        TurnState.Streaming => "streaming",
        TurnState.Tool => "tool",
        _ => "working",
    };
}
