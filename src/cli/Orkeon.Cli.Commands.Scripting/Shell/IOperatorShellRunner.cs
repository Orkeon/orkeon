namespace Orkeon.Cli.Commands.Scripting.Shell;

/// <summary>How an operator's shell command ended: its exit code, or that it met the timeout.</summary>
/// <param name="ExitCode">The process's exit code; null when it was stopped at the timeout.</param>
/// <param name="TimedOut">Whether the timeout stopped it.</param>
internal sealed record OperatorShellRun(int? ExitCode, bool TimedOut);

/// <summary>Starts the operator's shell on one command line: the seam the tests replace with a fake.</summary>
internal interface IOperatorShellRunner
{
    /// <summary>
    /// Runs <paramref name="command"/> and hands each line of its standard output and error to
    /// <paramref name="onLine"/> as it comes (from any thread, one call at a time per stream).
    /// </summary>
    /// <param name="command">The command line.</param>
    /// <param name="onLine">Receives each line.</param>
    /// <param name="timeout">The time after which the command is stopped.</param>
    /// <param name="cancellationToken">Stops the command and throws <see cref="OperationCanceledException"/>.</param>
    System.Threading.Tasks.Task<OperatorShellRun> RunAsync(string command, Action<string> onLine, TimeSpan timeout, CancellationToken cancellationToken);
}
