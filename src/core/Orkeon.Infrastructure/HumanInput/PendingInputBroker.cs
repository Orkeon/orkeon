using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Checkpointing;

namespace Orkeon.Infrastructure.HumanInput;

/// <summary>The in-memory <see cref="IPendingInputBroker"/>: one waiter per question, a run may hold on several (FWK-23).</summary>
public sealed class PendingInputBroker : IPendingInputBroker
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<(string Run, string Request), (long Order, TaskCompletionSource<string> Waiter)> _waiters = new();
    private readonly ConcurrentDictionary<string, Action<bool>> _watchers = new(StringComparer.Ordinal);
    private long _sequence;

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<string> WaitAsync(string runId, string requestId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var key = (runId, requestId);
        TaskCompletionSource<string> waiter;
        lock (_gate)
        {
            // The watcher is told under the gate, as the state changes: told outside it, a late "released" of the last
            // question could land after the "held" of the next one and restart the run clock during a hold (PR review HOST-2).
            var first = !HoldsAny(runId);
            waiter = _waiters.GetOrAdd(
                key,
                _ => (++_sequence, new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously))).Waiter;
            if (first)
                Notify(runId, true);
        }

        try
        {
            return await waiter.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (_waiters.TryRemove(key, out _) && !HoldsAny(runId))
                    Notify(runId, false);
            }
        }
    }

    /// <inheritdoc />
    public bool TryComplete(string runId, string? requestId, string answer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(answer);
        foreach (var (key, entry) in _waiters.OrderBy(static w => w.Value.Order))
        {
            if (string.Equals(key.Run, runId, StringComparison.Ordinal)
                && (requestId is null || string.Equals(key.Request, requestId, StringComparison.Ordinal)))
            {
                return entry.Waiter.TrySetResult(answer);
            }
        }

        return false;
    }

    /// <inheritdoc />
    public string? HeldRequestId(string runId) => HeldRequestIds(runId) is [var first, ..] ? first : null;

    /// <inheritdoc />
    public IReadOnlyList<string> HeldRequestIds(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return _waiters
            .Where(w => string.Equals(w.Key.Run, runId, StringComparison.Ordinal) && !w.Value.Waiter.Task.IsCompleted)
            .OrderBy(static w => w.Value.Order)
            .Select(static w => w.Key.Request)
            .ToArray();
    }

    /// <inheritdoc />
    public IDisposable WatchHolds(string runId, Action<bool> heldChanged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(heldChanged);
        lock (_gate)
        {
            _watchers[runId] = heldChanged;
            if (HoldsAny(runId))
                heldChanged(true);
        }

        return new Watch(this, runId, heldChanged);
    }

    private bool HoldsAny(string runId) =>
        _waiters.Keys.Any(k => string.Equals(k.Run, runId, StringComparison.Ordinal));

    // Called under _gate only, so the watcher sees the holds in the order they changed. The watcher must not call back
    // into the broker from another thread while it runs (the run deadline only takes its own lock).
    private void Notify(string runId, bool held)
    {
        if (_watchers.TryGetValue(runId, out var watcher))
            watcher(held);
    }

    private sealed class Watch(PendingInputBroker owner, string runId, Action<bool> handler) : IDisposable
    {
        public void Dispose() => owner._watchers.TryRemove(new KeyValuePair<string, Action<bool>>(runId, handler));
    }
}
