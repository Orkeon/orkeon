using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Domain.FileSystem;

namespace Orkeon.ConsoleApp.Replay;

/// <summary>
/// How <c>orkeon-repl</c>'s entry point drives <c>--replay</c>: the file read before the REPL opens (exit 2 when it
/// cannot be), the REPL run with the replay concluded whatever happens (exit 0 or 1 under <c>--replay</c> alone), and
/// the interrupt keys - Ctrl-C in the plain console, the TUI's interrupt key - that stop a replay.
/// </summary>
internal static class ReplayLaunch
{
    /// <summary>
    /// Reads the <c>--replay</c> file and starts the replay; null when there is none to replay or it started, else the
    /// exit code (<see cref="ReplayConsoleAdapter.UnreadableExitCode"/>), the error written on <paramref name="stderr"/>.
    /// </summary>
    /// <param name="replay">The replay's console.</param>
    /// <param name="fileSystem">The VFS the file is read through.</param>
    /// <param name="options">The command line.</param>
    /// <param name="stderr">Where an unreadable file is reported.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<int?> StartAsync(
        ReplayConsoleAdapter replay,
        IFileSystemService fileSystem,
        ScriptedCommandsCliOptions options,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(stderr);
        if (options.ReplayFile is not { } replayFile)
            return null;

        var load = await ReplayFile.LoadAsync(fileSystem, replayFile, cancellationToken).ConfigureAwait(false);
        if (load.Lines is not { } lines)
        {
            await stderr.WriteLineAsync($"orkeon-repl: {load.Error}").ConfigureAwait(false);
            return ReplayConsoleAdapter.UnreadableExitCode;
        }

        replay.TryStart(lines, closeInputAtEnd: !options.ReplayInteractive);
        return null;
    }

    /// <summary>
    /// Runs the REPL and concludes the replay once it ended, even by throwing (the replay then stops on
    /// <c>the REPL failed: &lt;message&gt;</c> and the exception goes on). Returns the process exit code under
    /// <c>--replay</c> alone (0 every line passed, 1 stopped on an error), null otherwise.
    /// </summary>
    /// <param name="replay">The replay's console.</param>
    /// <param name="options">The command line.</param>
    /// <param name="repl">The REPL's run.</param>
    public static async Task<int?> RunAsync(ReplayConsoleAdapter replay, ScriptedCommandsCliOptions options, Func<Task> repl)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(repl);
        string? crash = null;
        int exitCode;
        try
        {
            await repl().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            crash = $"the REPL failed: {ex.Message}";
            throw;
        }
        finally
        {
            exitCode = replay.Conclude(crash);
        }

        return options.ReplayNonInteractive ? exitCode : null;
    }

    /// <summary>
    /// Ctrl-C in the plain console. While a replay runs, the first press stops it and cancels the command in
    /// flight: true, the process must not end. A second press while the replay still runs (a command that ignores
    /// its cancellation), or a press with no replay running, keeps the key's default effect: false, the process
    /// ends (exit code 130 on Linux and macOS).
    /// </summary>
    /// <param name="replay">The replay's console.</param>
    /// <param name="cancelCommand">Cancels the command in flight (<c>IInteractiveRunner.RequestCommandCancellation</c>).</param>
    public static bool OnCancelKey(ReplayConsoleAdapter replay, Action cancelCommand)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(cancelCommand);
        if (replay.Interrupt() != ReplayInterrupt.Stopping)
            return false;
        cancelCommand();
        return true;
    }

    /// <summary>
    /// Lets the TUI's interrupt key (Esc-Esc) stop a replay: while one runs the key counts as a turn running, and
    /// the interrupt stops the replay before the prompt turn's own interrupt runs.
    /// </summary>
    /// <param name="integration">The TUI's host hooks, wired already (<c>TuiFidelityWiring.Wire</c>).</param>
    /// <param name="replay">The replay's console.</param>
    public static void WireTuiInterrupt(TuiIntegration integration, ReplayConsoleAdapter replay)
    {
        ArgumentNullException.ThrowIfNull(integration);
        ArgumentNullException.ThrowIfNull(replay);
        var turnRunning = integration.PromptTurnRunning;
        var interrupt = integration.InterruptCurrent;
        integration.PromptTurnRunning = () => replay.IsActive || turnRunning?.Invoke() == true;
        integration.InterruptCurrent = () =>
        {
            replay.Interrupt();
            interrupt?.Invoke();
        };
    }
}
