namespace Orkeon.Studio.Core.Process;

/// <summary>
/// The few operations <see cref="ProcessTerminator"/> needs on a running child.
/// Abstracted so the two-phase stop can be tested without spawning anything.
/// </summary>
public interface IProcessHandle
{
    /// <summary>Operating-system process id.</summary>
    int ProcessId { get; }

    /// <summary>True once the child has exited.</summary>
    bool HasExited { get; }

    /// <summary>
    /// Asks the child to stop the way Ctrl+C would, giving it the chance to run its own
    /// shutdown and return its interrupted exit code.
    /// </summary>
    /// <param name="failureReason">Why no signal was sent, when the method returns false.</param>
    /// <returns>False when this platform offers no per-child graceful stop, or the signal failed.</returns>
    bool TryRequestGracefulStop(out string? failureReason);

    /// <summary>Kills the child and every process it spawned.</summary>
    void Kill();

    /// <summary>Waits up to <paramref name="timeout"/> for the child to exit; false on timeout.</summary>
    Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
