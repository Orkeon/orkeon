using System.Collections.Concurrent;

namespace Orkeon.Host.Gateway;

/// <summary>
/// Coalesces progress updates before they reach a channel (spec §9).
/// <para>
/// A run emits an event per agent thought and per tool call. Relaying each one would exhaust
/// any chat platform's rate limit within a single crew — Discord's is per channel and unkind
/// about it. So progress is throttled, and the acknowledgement and the final answer are not:
/// those two carry meaning that cannot wait or be merged.
/// </para>
/// <para>
/// The window is **per conversation**. One responder serves every thread, and a shared window
/// would do two wrong things at once: starve all but one thread of progress, and — worse —
/// flush thread A's suppressed line into thread B when B finishes first, a structural
/// cross-conversation content leak in the very component the isolation story leans on.
/// </para>
/// <para>
/// The last suppressed update is **flushed on completion**, so a run whose last progress line
/// fell inside the window still ends with what it was doing rather than with silence.
/// </para>
/// </summary>
internal sealed class ThrottledResponder : IChatResponder
{
    /// <summary>How long a progress update waits before another may go out.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    private sealed class ConversationWindow
    {
        public DateTimeOffset LastSentAt = DateTimeOffset.MinValue;
        public string? Suppressed;
    }

    private readonly IChatResponder _inner;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, ConversationWindow> _windows = new(StringComparer.Ordinal);

    /// <summary>Wraps <paramref name="inner"/>, spacing its progress updates out.</summary>
    public ThrottledResponder(IChatResponder inner, TimeSpan? interval = null, TimeProvider? timeProvider = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _interval = interval ?? DefaultInterval;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task AcknowledgeAsync(InboundMessage message, string text, CancellationToken ct) =>
        _inner.AcknowledgeAsync(message, text, ct);

    /// <inheritdoc />
    public Task ProgressAsync(InboundMessage message, string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        var window = _windows.GetOrAdd(message.ConversationId, _ => new ConversationWindow());

        bool send;
        lock (window)
        {
            var now = _time.GetUtcNow();
            send = now - window.LastSentAt >= _interval;
            if (send)
            {
                window.LastSentAt = now;
                window.Suppressed = null;
            }
            else
            {
                // Keep only the newest: a user catching up wants where the run is now, not the
                // three places it passed through while the window was closed.
                window.Suppressed = text;
            }
        }

        return send ? _inner.ProgressAsync(message, text, ct) : Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(InboundMessage message, string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        // The window leaves with the conversation: a completed thread's state kept around
        // would be one entry per conversation, forever — daemon arithmetic.
        string? pending = null;
        if (_windows.TryRemove(message.ConversationId, out var window))
        {
            lock (window)
            {
                pending = window.Suppressed;
                window.Suppressed = null;
            }
        }

        if (pending is not null)
            await _inner.ProgressAsync(message, pending, ct).ConfigureAwait(false);

        await _inner.CompleteAsync(message, text, ct).ConfigureAwait(false);
    }
}
