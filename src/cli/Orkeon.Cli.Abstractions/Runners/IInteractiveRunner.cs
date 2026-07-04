namespace Orkeon.Cli.Abstractions.Runners;

/// <summary>
/// Surface that lets a host (typically a TUI) introspect and control the runner's
/// current command lifecycle. <see cref="InteractiveRunnerBase"/> implements this
/// to expose per-command cancellation and "is something running right now" state.
/// </summary>
public interface IInteractiveRunner
{
    /// <summary>Runs the REPL loop until the user exits or the token is cancelled.</summary>
    Task RunAsync(CancellationToken ct);

    /// <summary>
    /// True while a command's <c>ExecuteAsync</c> is in flight. Toggles back to false
    /// once the command finishes (success, failure, or cancellation).
    /// </summary>
    bool IsCommandRunning { get; }

    /// <summary>
    /// Name of the command currently in flight (matches <c>IInteractiveCommand.Name</c>).
    /// <c>null</c> when no command is running.
    /// </summary>
    string? CurrentCommandName { get; }

    /// <summary>
    /// UTC start timestamp of the command currently in flight. <c>null</c> when no
    /// command is running. Used by the TUI bandeau to render elapsed time.
    /// </summary>
    DateTime? CurrentCommandStartedAtUtc { get; }

    /// <summary>
    /// Cancels the in-flight command (if any) without affecting the outer REPL loop.
    /// No-op when <see cref="IsCommandRunning"/> is false. Cancellation propagates via
    /// the <see cref="CancellationToken"/> the command received in <c>ExecuteAsync</c>.
    /// </summary>
    void RequestCommandCancellation();
}
