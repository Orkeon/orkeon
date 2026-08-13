namespace Orkeon.Studio.Core.Process;

/// <summary>How a child process ended.</summary>
public enum ProcessTerminationMode
{
    /// <summary>It ended on its own.</summary>
    Exited,

    /// <summary>It was asked to stop gracefully (SIGINT) and did so within the grace period.</summary>
    StoppedBySignal,

    /// <summary>It was killed with its whole process tree — either after the grace period or
    /// because no graceful stop is available on this platform.</summary>
    Killed,

    /// <summary>It never started.</summary>
    NotStarted,
}

/// <summary>
/// The outcome of one child-process run. <see cref="RawExitCode"/> is what the OS reported;
/// <see cref="ExitCode"/> is what the user is shown — the two differ only when a cancelled
/// process was killed, which the OS reports as a signal death but Studio reports as the
/// CLI's own interrupted code (130), matching what the CLI would have returned had it been
/// given the time to exit by itself.
/// </summary>
public sealed record ProcessRunResult
{
    /// <summary>Exit code to display: <see cref="OrkeonExitCodes.Cancelled"/> when the run was cancelled.</summary>
    public required int ExitCode { get; init; }

    /// <summary>Exit code as reported by the operating system; -1 when the process never started.</summary>
    public required int RawExitCode { get; init; }

    /// <summary>Interpretation of <see cref="ExitCode"/>.</summary>
    public required RunOutcome Outcome { get; init; }

    /// <summary>How the process ended.</summary>
    public required ProcessTerminationMode Termination { get; init; }

    /// <summary>True when the run ended because its cancellation token fired.</summary>
    public bool WasCancelled { get; init; }

    /// <summary>Wall time between the spawn and the child's exit.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>User-facing one-liner explaining the outcome.</summary>
    public required string Description { get; init; }

    /// <summary>The run reached the child process and it exited on its own.</summary>
    public static ProcessRunResult FromExitCode(int exitCode, TimeSpan duration) => new()
    {
        ExitCode = exitCode,
        RawExitCode = exitCode,
        Outcome = OrkeonExitCodes.Classify(exitCode),
        Termination = ProcessTerminationMode.Exited,
        Duration = duration,
        Description = OrkeonExitCodes.Describe(exitCode),
    };

    /// <summary>
    /// The run was cancelled: whatever the OS reported, the user sees the CLI's interrupted code.
    /// </summary>
    public static ProcessRunResult FromCancellation(
        int rawExitCode,
        ProcessTerminationMode termination,
        TimeSpan duration) => new()
        {
            ExitCode = OrkeonExitCodes.Cancelled,
            RawExitCode = rawExitCode,
            Outcome = RunOutcome.Cancelled,
            Termination = termination,
            WasCancelled = true,
            Duration = duration,
            Description = termination == ProcessTerminationMode.Killed
                ? "Interrupted (exit code 130) — the process did not stop within the grace period and was killed."
                : "Interrupted (exit code 130).",
        };

    /// <summary>Nothing ran: <paramref name="reason"/> is the actionable message for the user.</summary>
    public static ProcessRunResult NotStarted(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ProcessRunResult
        {
            ExitCode = -1,
            RawExitCode = -1,
            Outcome = RunOutcome.NotStarted,
            Termination = ProcessTerminationMode.NotStarted,
            Duration = TimeSpan.Zero,
            Description = reason,
        };
    }
}
