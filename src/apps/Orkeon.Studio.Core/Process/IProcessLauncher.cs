namespace Orkeon.Studio.Core.Process;

/// <summary>
/// Spawns a child process and streams its output line by line while it runs.
/// It is an interface so every consumer of a run — the runner, the doctor reader,
/// the UIs' view models — can be exercised against a fake instead of a real
/// <c>orkeon</c> binary.
/// </summary>
public interface IProcessLauncher
{
    /// <summary>
    /// Runs <paramref name="request"/> to completion.
    /// <paramref name="onOutput"/> is invoked for each line as it is produced — never once at
    /// the end — and calls are serialized, so an implementation of it needs no locking of its
    /// own. Cancelling <paramref name="cancellationToken"/> stops the child (graceful first,
    /// kill after the grace period) and completes the returned task with a
    /// <see cref="RunOutcome.Cancelled"/> result rather than throwing.
    /// </summary>
    Task<ProcessRunResult> RunAsync(
        ProcessLaunchRequest request,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default);
}
