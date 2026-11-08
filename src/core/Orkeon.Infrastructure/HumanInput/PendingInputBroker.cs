using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Checkpointing;

namespace Orkeon.Infrastructure.HumanInput;

/// <summary>The in-memory <see cref="IPendingInputBroker"/>: one waiter per run, held on one question (FWK-23).</summary>
public sealed class PendingInputBroker : IPendingInputBroker
{
    private readonly ConcurrentDictionary<(string Run, string Request), TaskCompletionSource<string>> _waiters = new();

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<string> WaitAsync(string runId, string requestId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        var key = (runId, requestId);
        var waiter = _waiters.GetOrAdd(key, static _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            return await waiter.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _waiters.TryRemove(key, out _);
        }
    }

    /// <inheritdoc />
    public bool TryComplete(string runId, string? requestId, string answer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(answer);
        foreach (var (key, waiter) in _waiters)
        {
            if (string.Equals(key.Run, runId, StringComparison.Ordinal)
                && (requestId is null || string.Equals(key.Request, requestId, StringComparison.Ordinal)))
            {
                return waiter.TrySetResult(answer);
            }
        }

        return false;
    }

    /// <inheritdoc />
    public string? HeldRequestId(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        foreach (var (key, waiter) in _waiters)
        {
            if (string.Equals(key.Run, runId, StringComparison.Ordinal) && !waiter.Task.IsCompleted)
                return key.Request;
        }

        return null;
    }
}
