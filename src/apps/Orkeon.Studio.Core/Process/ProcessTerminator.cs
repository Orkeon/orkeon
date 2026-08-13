namespace Orkeon.Studio.Core.Process;

/// <summary>
/// How one stop attempt ended, and — when the graceful step could not be used — why.
/// The reason is kept rather than dropped: "killed" and "killed because this platform has no
/// per-child Ctrl+C" are different things to show a user staring at a truncated crew log.
/// </summary>
public sealed record ProcessTerminationOutcome
{
    /// <summary>How the process ended.</summary>
    public required ProcessTerminationMode Mode { get; init; }

    /// <summary>True when a graceful stop was actually delivered to the child.</summary>
    public bool GracefulStopRequested { get; init; }

    /// <summary>
    /// Why no graceful stop was delivered, as reported by
    /// <see cref="IProcessHandle.TryRequestGracefulStop"/>; <see langword="null"/> when one was.
    /// </summary>
    public string? GracefulStopFailureReason { get; init; }

    /// <summary>
    /// An outcome carrying only how the process ended — for a caller that never attempted a
    /// graceful stop, and so has nothing to report about one.
    /// </summary>
    public static ProcessTerminationOutcome Of(ProcessTerminationMode mode) => new() { Mode = mode };
}

/// <summary>
/// Stops a child process in two steps: ask first, kill second.
/// <para>
/// The graceful step matters because the CLI installs its own SIGINT/SIGTERM handler
/// (<c>RunCommand</c>): given the signal it unwinds the crew, flushes its output and exits
/// with 130. Killing straight away would lose all of that. The kill step matters because a
/// wedged child must not keep the UI waiting forever, hence the bounded grace period and
/// <c>entireProcessTree</c> — a crew can have spawned esbuild or a container CLI.
/// </para>
/// </summary>
public static class ProcessTerminator
{
    /// <summary>How long the kill step is given before we stop waiting and report anyway.</summary>
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Terminates <paramref name="handle"/>, returning how it actually ended.
    /// Never throws for a process that dies between two steps — the race is expected.
    /// </summary>
    /// <param name="handle">The child to stop.</param>
    /// <param name="gracePeriod">How long the child is given to honour the signal.</param>
    /// <param name="cancellationToken">Cancels the waits, not the stop itself.</param>
    public static async Task<ProcessTerminationOutcome> TerminateAsync(
        IProcessHandle handle,
        TimeSpan gracePeriod,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (handle.HasExited)
            return new ProcessTerminationOutcome { Mode = ProcessTerminationMode.Exited };

        var signalled = handle.TryRequestGracefulStop(out var failureReason);

        if (signalled
            && gracePeriod > TimeSpan.Zero
            && await handle.WaitForExitAsync(gracePeriod, cancellationToken).ConfigureAwait(false))
        {
            return new ProcessTerminationOutcome
            {
                Mode = ProcessTerminationMode.StoppedBySignal,
                GracefulStopRequested = true,
            };
        }

        if (handle.HasExited)
        {
            // It ended between the two steps. Crediting the signal for that is only honest
            // when a signal was actually delivered — on a platform that offers none, the child
            // simply finished on its own and the outcome must say so.
            return new ProcessTerminationOutcome
            {
                Mode = signalled
                    ? ProcessTerminationMode.StoppedBySignal
                    : ProcessTerminationMode.ExitedWithoutSignal,
                GracefulStopRequested = signalled,
                GracefulStopFailureReason = failureReason,
            };
        }

        handle.Kill();
        await handle.WaitForExitAsync(KillWait, cancellationToken).ConfigureAwait(false);
        return new ProcessTerminationOutcome
        {
            Mode = ProcessTerminationMode.Killed,
            GracefulStopRequested = signalled,
            GracefulStopFailureReason = failureReason,
        };
    }
}
