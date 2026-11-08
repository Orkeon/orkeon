namespace Orkeon.Application.Interfaces.Checkpointing;

/// <summary>
/// The in-process meeting point of a run that holds on a question and the caller that answers it (FWK-23): the daemon's
/// <c>POST /runs/{id}/input</c>, the stdin of <c>--events</c>. It keeps nothing across a restart: the persisted question does.
/// </summary>
public interface IPendingInputBroker
{
    /// <summary>Waits for the answer to <paramref name="requestId"/> of <paramref name="runId"/>.</summary>
    /// <param name="runId">The run.</param>
    /// <param name="requestId">The question.</param>
    /// <param name="ct">Cancels the wait.</param>
    System.Threading.Tasks.Task<string> WaitAsync(string runId, string requestId, CancellationToken ct);

    /// <summary>Hands the answer to the run that holds on the question; false when no run holds on it here.</summary>
    /// <param name="runId">The run.</param>
    /// <param name="requestId">The question, or null for the one the run holds on.</param>
    /// <param name="answer">The answer.</param>
    bool TryComplete(string runId, string? requestId, string answer);

    /// <summary>Gets the question the run holds on in this process, or null.</summary>
    /// <param name="runId">The run.</param>
    string? HeldRequestId(string runId);

    /// <summary>Gets every question the run holds on in this process, oldest first; empty when none.</summary>
    /// <param name="runId">The run.</param>
    IReadOnlyList<string> HeldRequestIds(string runId);

    /// <summary>
    /// Watches whether the run holds on any question: <paramref name="heldChanged"/> receives true when its first question
    /// is held and false when its last is released. One watcher per run; the run timeout stops its clock on it.
    /// </summary>
    /// <param name="runId">The run.</param>
    /// <param name="heldChanged">Receives the new state.</param>
    /// <returns>Stops the watch when disposed.</returns>
    IDisposable WatchHolds(string runId, Action<bool> heldChanged);
}
