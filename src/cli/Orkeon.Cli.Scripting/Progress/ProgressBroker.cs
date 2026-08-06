namespace Orkeon.Cli.Scripting.Progress;

/// <summary>
/// Host-side rendezvous between the operations that report progress (command scripts via
/// <c>ctx.progress</c>, crew scripts via the <c>progress_report</c> tool, framework hooks
/// like the codebase indexer) and the surfaces that render it (the TUI status line, the
/// agents pane). Single slot, last-write-wins.
/// </summary>
/// <remarks>
/// A single slot is deliberate: the REPL's long operations are serialised in practice
/// (the main loop is <c>maxConcurrent: 1</c>), and one status line can only show one bar.
/// <see cref="ProgressSnapshot.Ticket"/> keeps the door open for keying by instance if
/// concurrent operations ever become real. Thread-safe: written from pool threads and the
/// Jint engine thread, read from the Terminal.Gui UI timer.
/// </remarks>
public sealed class ProgressBroker
{
    private readonly object _gate = new();
    private ProgressSnapshot? _current;

    /// <summary>The operation currently reporting, or null when the slot is clear.</summary>
    public ProgressSnapshot? Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>
    /// Publishes a snapshot. When the label matches the current one the original
    /// <see cref="ProgressSnapshot.StartedAt"/> is preserved so the elapsed readout does
    /// not restart on every update.
    /// </summary>
    public void Report(ProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            var startedAt = _current is { } cur && string.Equals(cur.Label, snapshot.Label, StringComparison.Ordinal)
                ? cur.StartedAt
                : snapshot.StartedAt == default ? DateTimeOffset.UtcNow : snapshot.StartedAt;
            _current = snapshot with { StartedAt = startedAt };
        }
    }

    /// <summary>Clears the slot unconditionally.</summary>
    public void Clear()
    {
        lock (_gate) _current = null;
    }

    /// <summary>
    /// Clears the slot only if the current snapshot carries <paramref name="ticket"/> —
    /// the completion path of one instance must not erase a newer operation's progress.
    /// </summary>
    public void ClearTicket(string ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return;
        lock (_gate)
        {
            if (string.Equals(_current?.Ticket, ticket, StringComparison.Ordinal))
                _current = null;
        }
    }

    /// <summary>
    /// Clears the slot only if the current snapshot carries <paramref name="label"/> —
    /// same guard as <see cref="ClearTicket"/> for publishers that have no ticket.
    /// </summary>
    public void ClearLabel(string label)
    {
        if (string.IsNullOrEmpty(label)) return;
        lock (_gate)
        {
            if (string.Equals(_current?.Label, label, StringComparison.Ordinal))
                _current = null;
        }
    }
}
