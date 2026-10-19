using Orkeon.Cli.TerminalGui.Hosting;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Turns successive polls of the tasks list into transcript lines: a ticket that appears is a
/// task sent to the background (<c>● worker@crew(checks the design)</c> then
/// <c>  ⎿  Backgrounded agent (↓ tasks)</c>), a ticket that leaves the list is a task that
/// finished (<c>● Agent "worker@crew" finished · 2m 15s</c>, its last observed duration).
/// Pure: the rows come in, the lines go out; the list's view writes them.
/// </summary>
/// <remarks>
/// The list only carries live work, so a task that leaves it is reported as finished whatever
/// its outcome — the rows do not say whether it completed, failed or was cancelled.
/// </remarks>
internal sealed class TaskTranscriptTracker
{
    private readonly GlyphSet _glyphs;
    private readonly List<AgentRowInfo> _known = [];

    public TaskTranscriptTracker(GlyphSet glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        _glyphs = glyphs;
    }

    /// <summary>The lines this poll adds to the transcript: finished tasks first, then launched ones.</summary>
    public IReadOnlyList<string> Observe(IReadOnlyList<AgentRowInfo> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var current = rows.Where(r => !string.IsNullOrEmpty(r.Ticket)).ToList();
        var lines = new List<string>();

        foreach (var gone in _known.Where(k => !current.Exists(r => SameTicket(r, k))))
            lines.Add(FinishedLine(gone));
        foreach (var added in current.Where(r => !_known.Exists(k => SameTicket(r, k))))
            lines.AddRange(LaunchedLines(added));

        _known.Clear();
        _known.AddRange(current);
        return lines;
    }

    /// <summary><c>● Agent "name" finished · 2m 15s</c>; the duration is omitted when none was observed.</summary>
    internal string FinishedLine(AgentRowInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var duration = row.Elapsed is { } e ? $" {_glyphs.Dot} {StatusLineFormatter.FormatElapsed(e)}" : string.Empty;
        return $"{_glyphs.Bullet} Agent \"{row.Name}\" finished{duration}";
    }

    /// <summary><c>● name(intent)</c> then <c>  ⎿  Backgrounded agent (↓ tasks)</c>.</summary>
    internal IReadOnlyList<string> LaunchedLines(AgentRowInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var intent = string.IsNullOrWhiteSpace(row.Description) ? string.Empty : $"({row.Description.Trim()})";
        return
        [
            $"{_glyphs.Bullet} {row.Name}{intent}",
            $"{TranscriptModel.Indent}{_glyphs.Elbow}  Backgrounded agent (↓ tasks)",
        ];
    }

    private static bool SameTicket(AgentRowInfo a, AgentRowInfo b)
        => string.Equals(a.Ticket, b.Ticket, StringComparison.Ordinal);
}
