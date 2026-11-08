using Orkeon.Application.Interfaces.Checkpointing;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Delegating <see cref="IPendingInputStore"/> that holds the return of <see cref="AskAsync"/> for the question named
/// <see cref="HeldRequestId"/> until <see cref="Release"/> is called, after the question was persisted: the test orders two
/// concurrent askers by gates, never by delays.
/// </summary>
public sealed class GatedPendingInputStore(IPendingInputStore inner) : IPendingInputStore
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _persisted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The question whose <see cref="AskAsync"/> is held after it was persisted.</summary>
    public string? HeldRequestId { get; set; }

    /// <summary>Completes once the held question is persisted and its caller is waiting at the gate.</summary>
    public System.Threading.Tasks.Task Persisted => _persisted.Task;

    /// <summary>Lets the held caller go on.</summary>
    public void Release() => _gate.TrySetResult();

    public async System.Threading.Tasks.Task<PendingInputAsked> AskAsync(string sessionId, PendingInputRequest request, CancellationToken ct = default)
    {
        var asked = await inner.AskAsync(sessionId, request, ct);
        if (string.Equals(request.RequestId, HeldRequestId, StringComparison.Ordinal))
        {
            _persisted.TrySetResult();
            await _gate.Task.WaitAsync(ct);
        }

        return asked;
    }

    public System.Threading.Tasks.Task<PendingInputAnswered> AnswerAsync(string sessionId, string? requestId, string answer, CancellationToken ct = default) =>
        inner.AnswerAsync(sessionId, requestId, answer, ct);

    public System.Threading.Tasks.Task RecordAnswerAsync(string sessionId, PendingInputRequest request, string answer, CancellationToken ct = default) =>
        inner.RecordAnswerAsync(sessionId, request, answer, ct);

    public System.Threading.Tasks.Task SuspendAsync(string sessionId, string requestId, CancellationToken ct = default) =>
        inner.SuspendAsync(sessionId, requestId, ct);

    public System.Threading.Tasks.Task<PendingInputRequest?> GetPendingAsync(string sessionId, CancellationToken ct = default) =>
        inner.GetPendingAsync(sessionId, ct);
}
