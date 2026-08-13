namespace Orkeon.Studio.Core.Process;

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
    public static async Task<ProcessTerminationMode> TerminateAsync(
        IProcessHandle handle,
        TimeSpan gracePeriod,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (handle.HasExited)
            return ProcessTerminationMode.Exited;

        if (handle.TryRequestGracefulStop(out _) && gracePeriod > TimeSpan.Zero)
        {
            if (await handle.WaitForExitAsync(gracePeriod, cancellationToken).ConfigureAwait(false))
                return ProcessTerminationMode.StoppedBySignal;
        }

        if (handle.HasExited)
            return ProcessTerminationMode.StoppedBySignal;

        handle.Kill();
        await handle.WaitForExitAsync(KillWait, cancellationToken).ConfigureAwait(false);
        return ProcessTerminationMode.Killed;
    }
}
