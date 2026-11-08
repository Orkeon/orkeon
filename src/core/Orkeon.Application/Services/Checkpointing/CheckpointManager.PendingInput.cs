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

        var answers = state.AnsweredInputs;
        if (answers.TryGetValue(request.RequestId, out var answer))
        {
            if (answer.Answers(request))
                return new PendingInputAsked(answer.Answer, null);

            // The id is the same, the question is not (another tool, other arguments, another kind): the answer was given to
            // something else. It is dropped and the question is asked anew: it never approves a call nobody saw (fail closed).
            var kept = new Dictionary<string, AnsweredInput>(answers, StringComparer.Ordinal);
            kept.Remove(request.RequestId);
            answers = kept;
        }

        var open = state.PendingInputs;
        var index = IndexOf(open, request.RequestId);
        if (index >= 0 && ReferenceEquals(answers, state.AnsweredInputs) && SameQuestion(open[index], request))
        {
            // The question already asked wins, prompt included: a resumed run never rewords it.
            return new PendingInputAsked(null, open[index]);
        }

        // A new question, or one whose id now names another call: this one is what waits for an answer.
        var questions = open.ToList();
        if (index >= 0)
            questions[index] = request;
        else
            questions.Add(request);

        await _store.SaveAsync(
            state with
            {
                Phase = SessionPhase.WaitingForInput,
                PendingInputs = questions,
                AnsweredInputs = answers,
                UpdatedAt = DateTime.UtcNow,
            },
            ct).ConfigureAwait(false);
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

        // An answer that names no question goes to the one the run stopped on (the one its exit showed), else to the only one
        // open: with several open, it would go to an arbitrary one.
        var target = requestId ?? state.SuspendedOn;
        if (target is null)
        {
            if (state.PendingInputs.Count > 1)
                return new PendingInputAnswered(PendingInputAnswerOutcome.RequestIdRequired, null);
            target = state.PendingInputs.Count == 1 ? state.PendingInputs[0].RequestId : LastAnswered(state);
        }

        var index = target is null ? -1 : IndexOf(state.PendingInputs, target);
        if (index < 0)
        {
            // Answered, and the run has not resumed yet: the same answer given again (a retry after a crash) is the same fact.
            if (target is not null
                && state.Phase == SessionPhase.Suspended
                && state.AnsweredInputs.TryGetValue(target, out var given)
                && string.Equals(given.Answer, answer, StringComparison.Ordinal))
            {
                return new PendingInputAnswered(PendingInputAnswerOutcome.AlreadyAnswered, target);
            }

            return requestId is not null && state.PendingInputs.Count > 0
                ? new PendingInputAnswered(PendingInputAnswerOutcome.Mismatch, state.PendingInput!.RequestId)
                : new PendingInputAnswered(PendingInputAnswerOutcome.NotPending, null);
        }

        // Answered and waiting for a resume: the run that asked is gone (or will pick the answer up through the broker).
        var pending = state.PendingInputs[index];
        await _store.SaveAsync(Answered(state, pending, answer, SessionPhase.Suspended), ct).ConfigureAwait(false);
        return new PendingInputAnswered(PendingInputAnswerOutcome.Accepted, pending.RequestId);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task RecordAnswerAsync(string sessionId, PendingInputRequest request, string answer, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(answer);
        using var held = await EnterAsync(sessionId, ct).ConfigureAwait(false);
        if (await _store.GetAsync(sessionId, ct).ConfigureAwait(false) is { } state)
            await _store.SaveAsync(Answered(state, request, answer, SessionPhase.Running), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task SuspendAsync(string sessionId, string requestId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        using var held = await EnterAsync(sessionId, ct).ConfigureAwait(false);
        if (await _store.GetAsync(sessionId, ct).ConfigureAwait(false) is { SuspendedOn: null } state)
            await _store.SaveAsync(state with { SuspendedOn = requestId, UpdatedAt = DateTime.UtcNow }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<PendingInputRequest?> GetPendingAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return (await _store.GetAsync(sessionId, ct).ConfigureAwait(false))?.PendingInput;
    }

    // Closes the question answered, and only it: the questions other tasks asked stay open. The session runs again (or stays
    // suspended) once none is left; while one is, it still waits for input.
    private static SessionState Answered(SessionState state, PendingInputRequest request, string answer, SessionPhase phase)
    {
        var answers = new Dictionary<string, AnsweredInput>(state.AnsweredInputs, StringComparer.Ordinal)
        {
            [request.RequestId] = AnsweredInput.To(request, answer),
        };
        var open = state.PendingInputs.Where(p => !string.Equals(p.RequestId, request.RequestId, StringComparison.Ordinal)).ToList();
        var next = phase == SessionPhase.Running && open.Count > 0 ? SessionPhase.WaitingForInput : phase;
        return state with { Phase = next, PendingInputs = open, AnsweredInputs = answers, UpdatedAt = DateTime.UtcNow };
    }

    private static int IndexOf(IReadOnlyList<PendingInputRequest> questions, string requestId)
    {
        for (var i = 0; i < questions.Count; i++)
        {
            if (string.Equals(questions[i].RequestId, requestId, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static bool SameQuestion(PendingInputRequest persisted, PendingInputRequest asked) =>
        persisted.Kind == asked.Kind
        && string.Equals(persisted.ToolName, asked.ToolName, StringComparison.Ordinal)
        && string.Equals(persisted.ArgsHash, asked.ArgsHash, StringComparison.Ordinal);

    private static string? LastAnswered(SessionState state) =>
        state.AnsweredInputs.Count == 0 ? null : state.AnsweredInputs.MaxBy(a => a.Value.AnsweredAt).Key;
}
