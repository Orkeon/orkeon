using Orkeon.Cli.TerminalGui.Hosting;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Turns successive polls of the tasks list into transcript lines: a ticket that appears is a
/// task sent to the background (<c>● worker@crew(checks the design)</c> then
/// <c>  ⎿  Backgrounded agent (↓ tasks)</c>), a task that ended says how
/// (<c>● Agent "worker@crew" finished · 2m 15s</c>, <c>✗ … failed · …</c>, <c>⊘ … cancelled · …</c>).
/// Tasks that appear in one poll read as one group: <c>● 3 agents launched (↓ to manage)</c> then a
/// row per task. An end line says what the task used, when it is known.
/// Pure: the rows and the ended tasks come in, the lines go out; the list's view writes them.
/// </summary>
/// <remarks>
/// The rows only carry live work; the ended tasks (<see cref="TuiIntegration.FinishedAgentRows"/>)
/// carry the final state and duration. A task in that feed that no poll ever saw — it started
/// and ended between two polls — gets its launch lines then its end line, in that order. Each
/// ended ticket is reported once; the feed's tickets at the first poll are history, not news.
/// Without the feed, a task that leaves the list reads as finished, with its last observed
/// duration.
/// </remarks>
internal sealed class TaskTranscriptTracker
{
    private readonly GlyphSet _glyphs;
    private readonly List<AgentRowInfo> _known = [];
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private bool _baselined;

    /// <summary>The tool calls a ticket made so far; null when nothing counts them.</summary>
    internal Func<string, int>? ToolCalls { get; set; }

    public TaskTranscriptTracker(GlyphSet glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        _glyphs = glyphs;
    }

    /// <summary>
    /// The lines this poll adds to the transcript: the tasks that ended first (a never-seen one
    /// with its launch lines before its end line), then the launched ones.
    /// </summary>
    /// <param name="rows">The live rows of this poll.</param>
    /// <param name="finished">The ended tasks, read after <paramref name="rows"/>; null when unknown.</param>
    public IReadOnlyList<string> Observe(IReadOnlyList<AgentRowInfo> rows, IReadOnlyList<AgentRowInfo>? finished = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var ended = (finished ?? []).Where(r => !string.IsNullOrEmpty(r.Ticket)).ToList();
        if (finished is not null && !_baselined)
        {
            _baselined = true;
            foreach (var old in ended) _reported.Add(old.Ticket!);
        }

        // A ticket the feed already holds ended, even if the rows (read just before) still show it.
        var current = rows.Where(r => !string.IsNullOrEmpty(r.Ticket) && !ended.Exists(f => SameTicket(f, r))).ToList();
        var news = ended.Where(f => !_reported.Contains(f.Ticket!)).ToList();
        var lines = new List<string>();

        foreach (var gone in _known.Where(k => !current.Exists(r => SameTicket(r, k)) && !ended.Exists(f => SameTicket(f, k))))
        {
            lines.Add(FinishedLine(gone));
            _reported.Add(gone.Ticket!);
        }
        foreach (var end in news)
        {
            if (!_known.Exists(k => SameTicket(k, end))) lines.AddRange(LaunchedLines(end));
            lines.Add(FinishedLine(end));
            _reported.Add(end.Ticket!);
        }
        var added = current.Where(r => !_known.Exists(k => SameTicket(r, k))).ToList();
        lines.AddRange(added.Count == 1 ? LaunchedLines(added[0]) : GroupLines(added));

        // The feed is bounded by the host's retention: forget what it no longer holds.
        if (finished is not null) _reported.RemoveWhere(t => !ended.Exists(f => string.Equals(f.Ticket, t, StringComparison.Ordinal)));
        _known.Clear();
        _known.AddRange(current);
        return lines;
    }

    /// <summary>
    /// <c>● Agent "name" finished · 2m 15s</c>, <c>✗ Agent "name" failed · …</c> or
    /// <c>⊘ Agent "name" cancelled · …</c>, by the row's state (a live state reads as finished);
    /// the duration is omitted when none was observed.
    /// </summary>
    internal string FinishedLine(AgentRowInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var used = new List<string>(3);
        if (row.Ticket is { } ticket && ToolCalls?.Invoke(ticket) is > 0 and var calls)
            used.Add(calls == 1 ? "1 tool" : $"{calls} tools");
        if (row.Tokens is > 0 and var tokens) used.Add(StatusLineFormatter.FormatTokens(tokens));
        if (row.Elapsed is { } e) used.Add(StatusLineFormatter.FormatElapsed(e));
        var duration = string.Concat(used.Select(part => $" {_glyphs.Dot} {part}"));
        var (glyph, outcome) = row.State switch
        {
            TaskRowState.Failed => (_glyphs.Cross, "failed"),
            TaskRowState.Cancelled => (_glyphs.Slashed, "cancelled"),
            _ => (_glyphs.Bullet, "finished"),
        };
        return $"{glyph} Agent \"{row.Name}\" {outcome}{duration}";
    }

    /// <summary><c>● name(intent)</c> then <c>  ⎿  Backgrounded agent (↓ tasks)</c>.</summary>
    internal IReadOnlyList<string> LaunchedLines(AgentRowInfo row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var intent = string.IsNullOrWhiteSpace(row.Description) ? string.Empty : $"({row.Description.Trim()})";
        return
        [
            $"{_glyphs.Bullet} {row.Name}{intent}",
            $"{TranscriptModel.Indent}{_glyphs.Elbow}  Backgrounded agent ({_glyphs.Down} tasks)",
        ];
    }

    /// <summary><c>● 3 agents launched (↓ to manage)</c> then a branch per task; nothing for no task.</summary>
    internal IReadOnlyList<string> GroupLines(IReadOnlyList<AgentRowInfo> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return [];
        var rule = _glyphs.Box[9];
        var lines = new List<string>(rows.Count + 1) { $"{_glyphs.Bullet} {rows.Count} agents launched ({_glyphs.Down} to manage)" };
        for (var i = 0; i < rows.Count; i++)
        {
            var branch = i < rows.Count - 1 ? _glyphs.Box[3] : _glyphs.Box[6];
            var intent = string.IsNullOrWhiteSpace(rows[i].Description) ? string.Empty : $"({rows[i].Description.Trim()})";
            lines.Add($"{TranscriptModel.Indent}{branch}{rule} {rows[i].Name}{intent}");
        }
        return lines;
    }

    /// <summary>
    /// A line of this tracker with its display: an end line's glyph says how the task ended, the rest of a
    /// launch but its first line is secondary.
    /// </summary>
    internal TranscriptLine Styled(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.StartsWith(TranscriptModel.Indent, StringComparison.Ordinal))
        {
            // The branch or the elbow is dim; a task's name on a branch stays in the main colour.
            var elbow = line.AsSpan(TranscriptModel.Indent.Length).StartsWith(_glyphs.Elbow, StringComparison.Ordinal);
            var dim = elbow ? line.Length : line.IndexOf(' ', TranscriptModel.Indent.Length);
            return new TranscriptLine(line) { Runs = [new TranscriptRun(0, dim < 0 ? line.Length : dim, PaletteRole.Dim)] };
        }
        PaletteRole role;
        if (line.StartsWith(_glyphs.Cross, StringComparison.Ordinal)) role = PaletteRole.Error;
        else if (line.StartsWith(_glyphs.Slashed, StringComparison.Ordinal)) role = PaletteRole.Dim;
        else if (line.Contains("\" finished", StringComparison.Ordinal)) role = PaletteRole.Ok;
        else role = PaletteRole.Accent;
        var glyph = line.IndexOf(' ', StringComparison.Ordinal);
        return new TranscriptLine(line) { Runs = [new TranscriptRun(0, glyph < 0 ? line.Length : glyph, role)] };
    }

    private static bool SameTicket(AgentRowInfo a, AgentRowInfo b)
        => string.Equals(a.Ticket, b.Ticket, StringComparison.Ordinal);
}
