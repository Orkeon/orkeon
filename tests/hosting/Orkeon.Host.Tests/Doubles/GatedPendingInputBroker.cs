using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Infrastructure.HumanInput;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>
/// A <see cref="PendingInputBroker"/> whose run, once answered, wakes only when <see cref="Wake"/> is called: it widens
/// the moment between the answer handed to the run and the answer the run records in its session.
/// </summary>
internal sealed class GatedPendingInputBroker : IPendingInputBroker
{
    private readonly PendingInputBroker _inner = new();
    private readonly TaskCompletionSource _awake = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Lets every answered run go on.</summary>
    public void Wake() => _awake.TrySetResult();

    /// <inheritdoc />
    public async Task<string> WaitAsync(string runId, string requestId, CancellationToken ct)
    {
        var answer = await _inner.WaitAsync(runId, requestId, ct).ConfigureAwait(false);
        await _awake.Task.WaitAsync(ct).ConfigureAwait(false);
        return answer;
    }

    /// <inheritdoc />
    public bool TryComplete(string runId, string? requestId, string answer) => _inner.TryComplete(runId, requestId, answer);

    /// <inheritdoc />
    public string? HeldRequestId(string runId) => _inner.HeldRequestId(runId);

    /// <inheritdoc />
    public IReadOnlyList<string> HeldRequestIds(string runId) => _inner.HeldRequestIds(runId);

    /// <inheritdoc />
    public IDisposable WatchHolds(string runId, Action<bool> heldChanged) => _inner.WatchHolds(runId, heldChanged);
}
