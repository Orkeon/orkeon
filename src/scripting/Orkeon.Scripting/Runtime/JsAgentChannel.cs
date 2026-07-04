using System.Collections.Concurrent;
using Orkeon.Scripting.Exceptions;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Lightweight script-scoped A2A channel. Each agent has its own inbox; <c>send</c>
/// pushes a message, <c>ReceiveAsync</c> pops the next one (or blocks until one
/// arrives). <c>Broadcast</c> fans out to every other inbox.
/// </summary>
/// <remarks>
/// SCR-07 minimal: in-memory only, scoped to a single <see cref="JsCrew"/>. The full
/// Application <c>IAgentChannel</c> integration (typed payloads, multiple transports)
/// is wired in later; the DSL doesn't expose those concerns to scripts.
/// </remarks>
internal sealed class JsAgentChannel
{
    private readonly ConcurrentDictionary<string, Inbox> _inboxes = new(StringComparer.Ordinal);

    private Inbox GetInbox(string agentName) =>
        _inboxes.GetOrAdd(agentName, _ => new Inbox());

    public void Send(string targetName, object? payload)
        => GetInbox(targetName).Push(payload);

    public async Task<object?> ReceiveAsync(string selfName, TimeSpan? timeout, CancellationToken ct)
    {
        var inbox = GetInbox(selfName);
        return await inbox.PopAsync(timeout, ct).ConfigureAwait(false);
    }

    public void Broadcast(string fromName, object? payload, IEnumerable<string> peerNames)
    {
        foreach (var name in peerNames)
        {
            if (string.Equals(name, fromName, StringComparison.Ordinal)) continue;
            GetInbox(name).Push(payload);
        }
    }

    private sealed class Inbox
    {
        private readonly ConcurrentQueue<object?> _queue = new();
        private readonly object _lock = new();
        private TaskCompletionSource<object?>? _pendingWaiter;

        public void Push(object? message)
        {
            TaskCompletionSource<object?>? waiter = null;
            lock (_lock)
            {
                if (_pendingWaiter is not null)
                {
                    waiter = _pendingWaiter;
                    _pendingWaiter = null;
                }
                else
                {
                    _queue.Enqueue(message);
                }
            }
            waiter?.TrySetResult(message);
        }

        public async Task<object?> PopAsync(TimeSpan? timeout, CancellationToken ct)
        {
            TaskCompletionSource<object?> tcs;
            lock (_lock)
            {
                if (_queue.TryDequeue(out var existing))
                    return existing;
                if (_pendingWaiter is not null)
                    throw new InvalidOperationException("Only one waiter per inbox is supported in V1.");
                tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingWaiter = tcs;
            }

            using var ctReg = ct.Register(() =>
            {
                lock (_lock) { _pendingWaiter = null; }
                tcs.TrySetCanceled(ct);
            });

            if (timeout is null)
                return await tcs.Task.ConfigureAwait(false);

            using var timeoutCts = new CancellationTokenSource(timeout.Value);
            using var timeoutReg = timeoutCts.Token.Register(() =>
            {
                lock (_lock) { _pendingWaiter = null; }
                tcs.TrySetException(new ReceiveTimeoutException(timeout.Value));
            });
            return await tcs.Task.ConfigureAwait(false);
        }
    }
}
