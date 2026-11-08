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
        bool first;
        lock (_gate)
        {
            first = !HoldsAny(runId);
            waiter = _waiters.GetOrAdd(
                key,
                _ => (++_sequence, new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously))).Waiter;
        }

        if (first)
            Notify(runId, true);
        try
        {
            return await waiter.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            bool last;
            lock (_gate)
            {
                _waiters.TryRemove(key, out _);
                last = !HoldsAny(runId);
            }

            if (last)
                Notify(runId, false);
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
        _watchers[runId] = heldChanged;
        if (HoldsAny(runId))
            heldChanged(true);
        return new Watch(this, runId, heldChanged);
    }

    private bool HoldsAny(string runId) =>
        _waiters.Keys.Any(k => string.Equals(k.Run, runId, StringComparison.Ordinal));

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
