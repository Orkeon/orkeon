using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Session;

/// <summary>
/// In-memory, thread-safe <see cref="ISessionBufferService"/> for the single-session REPL
/// (exp 07 SPEC §7.4). The whole surface is guarded by one lock — contention is negligible
/// (a human-paced REPL) and it keeps the truncation/estimate logic obviously correct.
/// </summary>
public sealed class InMemorySessionBufferService : ISessionBufferService
{
    private readonly object _gate = new();
    private readonly List<SessionMessage> _messages = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private string? _title;
    private readonly string? _model;

    /// <summary>Creates a buffer, optionally tagging it with the active model name (informational).</summary>
    public InMemorySessionBufferService(string? model = null)
    {
        _model = model;
    }

    /// <inheritdoc />
    public int MessageCount
    {
        get { lock (_gate) return _messages.Count; }
    }

    /// <inheritdoc />
    public IReadOnlyList<SessionMessage> GetMessages()
    {
        lock (_gate) return _messages.ToArray();
    }

    /// <inheritdoc />
    public void ReplaceMessages(IReadOnlyList<SessionMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        lock (_gate)
        {
            _messages.Clear();
            _messages.AddRange(messages);
        }
    }

    /// <inheritdoc />
    public bool AppendNote(string note)
    {
        if (string.IsNullOrWhiteSpace(note)) return false;
        lock (_gate)
        {
            _messages.Add(new SessionMessage { Role = "note", Content = note });
            return true;
        }
    }

    /// <inheritdoc />
    public SessionMetadata GetMetadata()
    {
        lock (_gate)
        {
            return new SessionMetadata
            {
                SessionId = _sessionId,
                Title = _title,
                Model = _model,
                MessageCount = _messages.Count,
                EstimatedTokens = EstimateTokenCountUnlocked(),
            };
        }
    }

    /// <inheritdoc />
    public bool SetTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        lock (_gate) { _title = title.Trim(); }
        return true;
    }

    /// <inheritdoc />
    public int Truncate(int retainCount)
    {
        if (retainCount < 0) retainCount = 0;
        lock (_gate)
        {
            var total = _messages.Count;
            if (total == 0) return 0;

            // Keep the head message (system/context anchor) + the last `retainCount` messages.
            var keepTail = Math.Min(retainCount, total);
            var headKept = total > keepTail ? 1 : 0; // only keep a distinct head when it isn't already in the tail window
            var kept = Math.Min(total, headKept + keepTail);
            if (kept >= total) return 0;

            var head = headKept == 1 ? _messages[0] : null;
            var tail = _messages.GetRange(total - keepTail, keepTail);

            _messages.Clear();
            if (head is not null) _messages.Add(head);
            _messages.AddRange(tail);

            return total - _messages.Count;
        }
    }

    /// <inheritdoc />
    public int Reset()
    {
        lock (_gate)
        {
            var removed = _messages.Count;
            _messages.Clear();
            _title = null;
            return removed;
        }
    }

    /// <inheritdoc />
    public int EstimateTokenCount()
    {
        lock (_gate) return EstimateTokenCountUnlocked();
    }

    private int EstimateTokenCountUnlocked()
    {
        long chars = 0;
        foreach (var m in _messages)
            chars += (m.Role?.Length ?? 0) + (m.Content?.Length ?? 0);
        return (int)Math.Min(int.MaxValue, chars / 4);
    }
}
