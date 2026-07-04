namespace Orkeon.Application.Crew;

/// <summary>
/// Hook invoked at key lifecycle points of a crew execution.
/// Implementations receive task-level and crew-level snapshots so they can persist
/// summaries, metrics, or diagnostics regardless of whether the crew succeeded or failed.
/// </summary>
public interface ICrewExecutionHook
{
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
    /// Called when the crew is canceled (timeout) or fails with an unexpected exception.
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
}
