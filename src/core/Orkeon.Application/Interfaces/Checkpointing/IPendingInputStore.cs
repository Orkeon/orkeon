namespace Orkeon.Application.Interfaces.Checkpointing;

/// <summary>
/// Where a run's questions to a human are kept with its checkpoint session (FWK-23): the same store as the
/// task checkpoints, never a second one. Every method is atomic for its session.
/// </summary>
public interface IPendingInputStore
{
    /// <summary>
    /// Puts a question to the session. When the session holds an answer given to the same question under its
    /// <see cref="PendingInputRequest.RequestId"/> (the same kind and, for a tool approval, the same tool and arguments) the
    /// answer is returned and nothing changes; an answer given to another question under that id is dropped and the question
    /// is asked anew: an answer never approves another call. When the session already waits on that same question the
    /// persisted one is returned (its prompt wins); otherwise the question is persisted next to the others still open and the
    /// session waits for input. Every question is persisted: several wait at once, one per request id.
    /// </summary>
    /// <param name="sessionId">The run's session.</param>
    /// <param name="request">The question.</param>
    /// <param name="ct">Cancellation.</param>
    System.Threading.Tasks.Task<PendingInputAsked> AskAsync(string sessionId, PendingInputRequest request, CancellationToken ct = default);

    /// <summary>
    /// Answers a pending question: the first answer wins (compare-and-set on the request id), the next one is refused. Only
    /// the question answered is closed: the others stay open.
    /// </summary>
    /// <param name="sessionId">The run's session.</param>
    /// <param name="requestId">
    /// The question answered, or null for the question the run stopped on (<see cref="SessionState.SuspendedOn"/>), else the
    /// only one open; null while several are open and the run stopped on none is refused
    /// (<see cref="PendingInputAnswerOutcome.RequestIdRequired"/>).
    /// </param>
    /// <param name="answer">The answer, as text (a confirmation is yes or no, several choices a JSON array).</param>
    /// <param name="ct">Cancellation.</param>
    System.Threading.Tasks.Task<PendingInputAnswered> AnswerAsync(string sessionId, string? requestId, string answer, CancellationToken ct = default);

    /// <summary>
    /// Records an answer a run received while it held on its question: that question is closed, and the session runs again
    /// once no other question is open.
    /// </summary>
    /// <param name="sessionId">The run's session.</param>
    /// <param name="request">The question answered.</param>
    /// <param name="answer">The answer.</param>
    /// <param name="ct">Cancellation.</param>
    System.Threading.Tasks.Task RecordAnswerAsync(string sessionId, PendingInputRequest request, string answer, CancellationToken ct = default);

    /// <summary>
    /// Records the question the run stopped on (the one its exit names): an answer that names no question goes to it.
    /// The first one recorded in a run wins, as on <see cref="Orkeon.Application.Interfaces.Services.RunScope.Suspend"/>.
    /// </summary>
    /// <param name="sessionId">The run's session.</param>
    /// <param name="requestId">The question, persisted with the session.</param>
    /// <param name="ct">Cancellation.</param>
    System.Threading.Tasks.Task SuspendAsync(string sessionId, string requestId, CancellationToken ct = default);

    /// <summary>Gets the question shown for the session (<see cref="SessionState.PendingInput"/>), or null.</summary>
    /// <param name="sessionId">The run's session.</param>
    /// <param name="ct">Cancellation.</param>
    System.Threading.Tasks.Task<PendingInputRequest?> GetPendingAsync(string sessionId, CancellationToken ct = default);
}

/// <summary>What <see cref="IPendingInputStore.AskAsync"/> found.</summary>
/// <param name="Answer">The answer already given to the question, or null when it is still open.</param>
/// <param name="Pending">The question as persisted (null when an answer exists or the session does not).</param>
public sealed record PendingInputAsked(string? Answer, PendingInputRequest? Pending);

/// <summary>What <see cref="IPendingInputStore.AnswerAsync"/> did.</summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="RequestId">The question the answer went to when accepted.</param>
public sealed record PendingInputAnswered(PendingInputAnswerOutcome Outcome, string? RequestId);

/// <summary>The outcome of an answer.</summary>
public enum PendingInputAnswerOutcome
{
    /// <summary>The answer was recorded.</summary>
    Accepted,

    /// <summary>The session waits on no question (never asked, or already answered: the first answer won).</summary>
    NotPending,

    /// <summary>The session waits on another question than the one named.</summary>
    Mismatch,

    /// <summary>The session does not exist.</summary>
    UnknownSession,

    /// <summary>
    /// The question named (or the one the run stopped on) was answered with this same text and the run has not resumed
    /// yet: the answer is already stored (a retry after a crash), nothing changes.
    /// </summary>
    AlreadyAnswered,

    /// <summary>The session waits on several questions, stopped on none of them, and the answer names none.</summary>
    RequestIdRequired,
}
