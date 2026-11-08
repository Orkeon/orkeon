using Orkeon.Application.Interfaces.Checkpointing;

namespace Orkeon.Application.Services.Checkpointing;

// The questions of a run live in its checkpoint session (FWK-23), behind the same per-session lock as its task checkpoints.
public sealed partial class CheckpointManager
{
    /// <inheritdoc />
    public async System.Threading.Tasks.Task<PendingInputAsked> AskAsync(string sessionId, PendingInputRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(request);
        using var held = await EnterAsync(sessionId, ct).ConfigureAwait(false);
        var state = await _store.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (state is null)
            return new PendingInputAsked(null, request);

        if (state.AnsweredInputs.TryGetValue(request.RequestId, out var answer))
            return new PendingInputAsked(answer, null);

        if (state.PendingInput is { } pending)
        {
            // The question already asked wins, prompt included: a resumed run never rewords it. Another question
            // while one waits is not persisted: one waits at a time.
            return string.Equals(pending.RequestId, request.RequestId, StringComparison.Ordinal)
                ? new PendingInputAsked(null, pending)
                : new PendingInputAsked(null, null);
        }

        await _store.SaveAsync(
            state with { Phase = SessionPhase.WaitingForInput, PendingInput = request, UpdatedAt = DateTime.UtcNow }, ct).ConfigureAwait(false);
        return new PendingInputAsked(null, request);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<PendingInputAnswered> AnswerAsync(string sessionId, string? requestId, string answer, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(answer);
        using var held = await EnterAsync(sessionId, ct).ConfigureAwait(false);
        var state = await _store.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (state is null)
            return new PendingInputAnswered(PendingInputAnswerOutcome.UnknownSession, null);

        if (state.PendingInput is not { } pending)
        {
            // Answered, and the run has not resumed yet: the same answer given again (a retry after a crash) is the same fact.
            if (state.Phase == SessionPhase.Suspended && state.AnsweredInputs.Count > 0
                && state.AnsweredInputs.Last() is var last
                && (requestId is null || string.Equals(last.Key, requestId, StringComparison.Ordinal))
                && string.Equals(last.Value, answer, StringComparison.Ordinal))
            {
                return new PendingInputAnswered(PendingInputAnswerOutcome.AlreadyAnswered, last.Key);
            }

            return new PendingInputAnswered(PendingInputAnswerOutcome.NotPending, null);
        }

        if (requestId is not null && !string.Equals(pending.RequestId, requestId, StringComparison.Ordinal))
            return new PendingInputAnswered(PendingInputAnswerOutcome.Mismatch, pending.RequestId);

        // Answered and waiting for a resume: the run that asked is gone (or will pick the answer up through the broker).
        await _store.SaveAsync(Answered(state, pending.RequestId, answer, SessionPhase.Suspended), ct).ConfigureAwait(false);
        return new PendingInputAnswered(PendingInputAnswerOutcome.Accepted, pending.RequestId);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task RecordAnswerAsync(string sessionId, string requestId, string answer, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(answer);
        using var held = await EnterAsync(sessionId, ct).ConfigureAwait(false);
        if (await _store.GetAsync(sessionId, ct).ConfigureAwait(false) is { } state)
            await _store.SaveAsync(Answered(state, requestId, answer, SessionPhase.Running), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<PendingInputRequest?> GetPendingAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return (await _store.GetAsync(sessionId, ct).ConfigureAwait(false))?.PendingInput;
    }

    private static SessionState Answered(SessionState state, string requestId, string answer, SessionPhase phase)
    {
        var answers = new Dictionary<string, string>(state.AnsweredInputs, StringComparer.Ordinal) { [requestId] = answer };
        return state with { Phase = phase, PendingInput = null, AnsweredInputs = answers, UpdatedAt = DateTime.UtcNow };
    }
}
