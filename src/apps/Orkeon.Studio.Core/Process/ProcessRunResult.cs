namespace Orkeon.Studio.Core.Process;

/// <summary>How a child process ended.</summary>
public enum ProcessTerminationMode
{
    /// <summary>It ended on its own.</summary>
    Exited,

    /// <summary>It was asked to stop gracefully (SIGINT) and did so within the grace period.</summary>
    StoppedBySignal,

    /// <summary>
    /// It ended on its own during the stop attempt, without ever being signalled — the
    /// graceful stop could not be delivered (see
    /// <see cref="ProcessRunResult.GracefulStopFailureReason"/>) and the kill was never needed.
    /// </summary>
    ExitedWithoutSignal,

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

    /// <summary>
    /// Why the child could not be asked to stop gracefully, when that step was unavailable;
    /// <see langword="null"/> when a signal was delivered or none was needed.
    /// </summary>
    public string? GracefulStopFailureReason { get; init; }

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
    /// <param name="rawExitCode">Exit code as reported by the operating system.</param>
    /// <param name="termination">How the stop attempt ended, and why the signal step failed if it did.</param>
    /// <param name="duration">Wall time of the run.</param>
    public static ProcessRunResult FromCancellation(
        int rawExitCode,
        ProcessTerminationOutcome termination,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(termination);

        return new ProcessRunResult
        {
            ExitCode = OrkeonExitCodes.Cancelled,
            RawExitCode = rawExitCode,
            Outcome = RunOutcome.Cancelled,
            Termination = termination.Mode,
            WasCancelled = true,
            Duration = duration,
            GracefulStopFailureReason = termination.GracefulStopFailureReason,
            Description = DescribeCancellation(termination),
        };
    }

    private static string DescribeCancellation(ProcessTerminationOutcome termination)
    {
        const string Interrupted = "Interrupted (exit code 130)";
        var reason = termination.GracefulStopFailureReason is { Length: > 0 } text
            ? $" No stop signal could be sent: {text}."
            : "";

        return termination.Mode switch
        {
            ProcessTerminationMode.Killed =>
                Interrupted + " — the process did not stop within the grace period and was killed." + reason,
            ProcessTerminationMode.ExitedWithoutSignal =>
                Interrupted + " — the process ended on its own before it could be killed." + reason,
            _ => Interrupted + ".",
        };
    }

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
