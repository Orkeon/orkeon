namespace Orkeon.Application.Crew;

/// <summary>
/// Hook invoked at key lifecycle points of a crew execution.
/// Implementations receive task-level and crew-level snapshots so they can persist
/// summaries, metrics, or diagnostics regardless of whether the crew succeeded or failed.
/// </summary>
public interface ICrewExecutionHook
{
    /// <summary>
    /// Called each time a task starts inside a crew run, before its agent is asked anything.
    /// Every orchestration mode dispatches it, so a watcher can show what is in progress
    /// rather than only what has finished.
    /// </summary>
    /// <param name="snapshot">Which task, which agent, since when.</param>
    /// <param name="ct">Cancellation token.</param>
    System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartSnapshot snapshot, CancellationToken ct);

    /// <summary>
    /// Called each time a task completes (successfully or not) inside a crew run.
    /// </summary>
    /// <param name="snapshot">Snapshot of the completed task.</param>
    /// <param name="ct">Cancellation token.</param>
    System.Threading.Tasks.Task OnTaskCompletedAsync(TaskExecutionSnapshot snapshot, CancellationToken ct);

    /// <summary>
    /// Called when the entire crew completes successfully.
    /// </summary>
    /// <param name="snapshot">Accumulated snapshot of all tasks in the run.</param>
    /// <param name="ct">Cancellation token.</param>
    System.Threading.Tasks.Task OnCrewCompletedAsync(CrewExecutionSnapshot snapshot, CancellationToken ct);

    /// <summary>
    /// Called once when the crew run fails or is canceled: a task that did not succeed, an
    /// exception, a timeout — and a run that fails before its strategy, such as a memory that cannot
    /// work or a plan whose provider failed, which the orchestrator reports itself (GAP-32).
    /// Implementations must be robust — any exception thrown here is swallowed by the caller.
    /// </summary>
    /// <param name="isPartial">Partial snapshot of whatever was completed before the failure.</param>
    /// <param name="ex">
    /// The exception that caused the failure, or <c>null</c> if the failure reason is unknown.
    /// For timeouts this is typically an <see cref="OperationCanceledException"/>.
    /// </param>
    /// <param name="ct">
    /// A <b>fresh, uncanceled</b> token that the implementation should use for its own I/O
    /// (the original crew token may already be canceled).
    /// </param>
    System.Threading.Tasks.Task OnCrewFailedAsync(CrewExecutionSnapshot isPartial, Exception? ex, CancellationToken ct);

    /// <summary>
    /// Called when the run puts a question to a human that outlives the task (FWK-23), and when the answer comes.
    /// The default does nothing: only the observers that publish run events care.
    /// </summary>
    /// <param name="notice">The question, or the answer to it.</param>
    /// <param name="ct">Cancellation token.</param>
    System.Threading.Tasks.Task OnHumanInputAsync(HumanInputNotice notice, CancellationToken ct) =>
        System.Threading.Tasks.Task.CompletedTask;
}

/// <summary>What a durable human-input provider tells the run's observers (FWK-23).</summary>
public enum HumanInputNoticeKind
{
    /// <summary>A question was put to a human: <c>input.needed</c>.</summary>
    Needed,

    /// <summary>The question was answered: <c>input.given</c>.</summary>
    Given,
}

/// <summary>A question to a human, or its answer, as the observers of a run see it.</summary>
/// <param name="Kind">Whether the question was asked or answered.</param>
/// <param name="Request">The question as persisted.</param>
/// <param name="Suspending">True when the run stops on the question (<c>Suspend</c>), false when it holds on it.</param>
public sealed record HumanInputNotice(
    HumanInputNoticeKind Kind,
    Orkeon.Application.Interfaces.Checkpointing.PendingInputRequest Request,
    bool Suspending = false);
