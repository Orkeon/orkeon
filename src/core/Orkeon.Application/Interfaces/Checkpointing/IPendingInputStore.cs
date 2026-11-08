namespace Orkeon.Application.Interfaces.Checkpointing;

/// <summary>
/// Where a run's questions to a human are kept with its checkpoint session (FWK-23): the same store as the
/// task checkpoints, never a second one. Every method is atomic for its session.
/// </summary>
public interface IPendingInputStore
{
    /// <summary>
    /// Puts a question to the session. When the session already holds an answer for its
    /// <see cref="PendingInputRequest.RequestId"/> the answer is returned and nothing changes; when it already waits on that
    /// same question the persisted one is returned (its prompt wins); otherwise the question is persisted and the session
    /// waits for input. A second question while another one is pending is not persisted: only one waits at a time.
    /// </summary>
    /// <param name="sessionId">The run's session.</param>
    /// <param name="request">The question.</param>
    /// <param name="ct">Cancellation.</param>
    System.Threading.Tasks.Task<PendingInputAsked> AskAsync(string sessionId, PendingInputRequest request, CancellationToken ct = default);

    /// <summary>
    /// Answers the pending question: the first answer wins (compare-and-set on the request id), the next one is refused.
    /// </summary>
    /// <param name="sessionId">The run's session.</param>
    /// <param name="requestId">The question answered, or null for whichever is pending.</param>
    /// <param name="answer">The answer, as text (a confirmation is yes or no, several choices a JSON array).</param>
    /// <param name="ct">Cancellation.</param>
    System.Threading.Tasks.Task<PendingInputAnswered> AnswerAsync(string sessionId, string? requestId, string answer, CancellationToken ct = default);

    /// <summary>Records an answer a run received while it held on its question: the session is running again.</summary>
    /// <param name="sessionId">The run's session.</param>
    /// <param name="requestId">The question answered.</param>
    /// <param name="answer">The answer.</param>
    /// <param name="ct">Cancellation.</param>
    System.Threading.Tasks.Task RecordAnswerAsync(string sessionId, string requestId, string answer, CancellationToken ct = default);

    /// <summary>Gets the question the session waits on, or null.</summary>
    /// <param name="sessionId">The run's session.</param>
    /// <param name="ct">Cancellation.</param>
    System.Threading.Tasks.Task<PendingInputRequest?> GetPendingAsync(string sessionId, CancellationToken ct = default);
}

/// <summary>What <see cref="IPendingInputStore.AskAsync"/> found.</summary>
/// <param name="Answer">The answer already given to the question, or null when it is still open.</param>
/// <param name="Pending">The question as persisted (null when an answer exists or when another question holds the slot).</param>
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
    /// The session waits on no question because the last one was answered with this same text and the run has not resumed
    /// yet: the answer is already stored (a retry after a crash), nothing changes.
    /// </summary>
    AlreadyAnswered,
}
