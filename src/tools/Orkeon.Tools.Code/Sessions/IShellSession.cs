namespace Orkeon.Tools.Code.Sessions;

/// <summary>One command a shell session runs, already validated by the tool.</summary>
/// <param name="Executable">The program, a bare name looked up on the session's <c>PATH</c>.</param>
/// <param name="Arguments">Its arguments, each passed as one word whatever it holds.</param>
/// <param name="WorkingDirectory">
/// The directory the command runs in, as the session sees it (virtual when confined, physical
/// otherwise); null or empty keeps the session's own directory.
/// </param>
/// <param name="Timeout">How long the command may run before the session gives up on it.</param>
internal sealed record ShellInvocation(
    string Executable,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    TimeSpan Timeout);

/// <summary>What a command run by a shell session produced.</summary>
/// <param name="ExitCode">The command's exit code; -1 when it timed out.</param>
/// <param name="Stdout">Its standard output, capped by the session.</param>
/// <param name="Stderr">Its standard error, capped by the session.</param>
/// <param name="TimedOut">Whether the command outlived its timeout.</param>
/// <param name="SessionReset">
/// Whether the session was killed after this command (a timeout), so the next command starts in a
/// new one.
/// </param>
internal sealed record ShellRunResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut = false,
    bool SessionReset = false);

/// <summary>
/// A shell an agent keeps for its run: every command of the run goes through it, one at a time.
/// Disposing it ends the shell and everything it started; it never throws and does not depend on
/// the caller awaiting it (the end of a scripted agent's scope is fire-and-forget).
/// </summary>
internal interface IShellSession : IAsyncDisposable
{
    /// <summary>
    /// Runs one command. Concurrent calls queue. A timeout returns a result with
    /// <see cref="ShellRunResult.TimedOut"/> and leaves the session unusable; the caller's
    /// cancellation throws <see cref="OperationCanceledException"/> and leaves it unusable too.
    /// </summary>
    /// <exception cref="ShellSessionException">The session is closed or its shell died.</exception>
    Task<ShellRunResult> RunAsync(ShellInvocation invocation, CancellationToken cancellationToken);
}
