using System.Diagnostics;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Cli.Commands.Scripting.Shell;

/// <summary>
/// Runs a command in the operator's own shell, as their terminal would: <c>$SHELL -c</c> (<c>/bin/sh</c> when unset),
/// <c>%ComSpec% /d /s /c "command"</c> on Windows, in the host's working folder, with the host's whole environment plus
/// <c>PAGER=cat</c> and <c>GIT_PAGER=cat</c>. Its standard input is closed at once, so a command that waits for the
/// keyboard reads the end of its input — it fails or meets the timeout, and never takes the console. On a POSIX host
/// with <c>setsid</c> on <c>PATH</c> the shell starts in a session of its own, without a controlling terminal, so a
/// command that opens <c>/dev/tty</c> (a <c>sudo</c> or <c>ssh</c> password prompt) fails instead of reading the
/// keyboard; without <c>setsid</c> (macOS) the shell starts directly and such a prompt can still read it.
/// The run ends when the shell ends: a background child that keeps the output open (<c>sleep 300 &amp;</c>) is left
/// running, and what it prints after a short grace is not read.
/// </summary>
internal sealed class ProcessOperatorShellRunner : IOperatorShellRunner
{
    /// <summary>How long the streams are drained after the process tree was killed.</summary>
    private static readonly TimeSpan s_drainAfterKill = TimeSpan.FromSeconds(5);

    /// <summary>How long the streams are drained after the shell ended, for a child that still holds them.</summary>
    private static readonly TimeSpan s_drainAfterExit = TimeSpan.FromSeconds(2);

    private static readonly Lazy<string?> s_setsid = new(ProbeSetsid);

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<OperatorShellRun> RunAsync(
        string command, Action<string> onLine, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(onLine);
        cancellationToken.ThrowIfCancellationRequested();

        var reading = 1;
        void OnData(DataReceivedEventArgs e)
        {
            if (e.Data is not null && Volatile.Read(ref reading) == 1)
                onLine(e.Data);
        }

        using var process = new Process
        {
            StartInfo = StartInfo(command, OperatingSystem.IsWindows(), SetsidPath()),
            EnableRaisingEvents = true,
        };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult();
        process.OutputDataReceived += (_, e) => OnData(e);
        process.ErrorDataReceived += (_, e) => OnData(e);
        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            // The shell's exit, not the end of its streams: a background child may hold them open for long.
            await exited.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            await DrainAsync(process).ConfigureAwait(false);
            return new OperatorShellRun(process.ExitCode, TimedOut: false);
        }
        catch (OperationCanceledException)
        {
            Stop(process);
            cancellationToken.ThrowIfCancellationRequested();
            return new OperatorShellRun(null, TimedOut: true);
        }
        finally
        {
            Volatile.Write(ref reading, 0);
            StopReading(process);
        }
    }

    /// <summary>The path of <c>setsid</c> on <c>PATH</c>, on a POSIX host; null on Windows or when it is absent.</summary>
    internal static string? SetsidPath() => s_setsid.Value;

    /// <summary>Waits a short grace for the streams to end once the shell ended.</summary>
    private static async System.Threading.Tasks.Task DrainAsync(Process process)
    {
        using var grace = new CancellationTokenSource(s_drainAfterExit);
        try
        {
            await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A background child still holds the output: what it prints from now on is not read.
        }
    }

    private static void StopReading(Process process)
    {
        try
        {
            process.CancelOutputRead();
            process.CancelErrorRead();
        }
        catch (InvalidOperationException)
        {
            // The reads were never started or already ended.
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It ended between the cancellation and the kill.
        }

        process.WaitForExit(s_drainAfterKill);
    }

    /// <summary>
    /// How the shell is started. On Windows the command is wrapped in one pair of quotes after <c>/d /s /c</c>: cmd
    /// strips exactly that outer pair and keeps the command's own quotes (<c>"C:\Program Files\x.exe" "a b"</c>), and
    /// <c>/d</c> skips the AutoRun commands of the registry. On POSIX the shell gets <c>-c</c> and the command as two
    /// arguments, through <paramref name="setsid"/> when given.
    /// </summary>
    internal static ProcessStartInfo StartInfo(string command, bool windows, string? setsid)
    {
        ProcessStartInfo info;
        if (windows)
        {
            // OUT-OF-SCOPE: the operator's command interpreter, probed from the host's environment.
            var comSpec = Environment.GetEnvironmentVariable("ComSpec");
            info = new ProcessStartInfo(string.IsNullOrWhiteSpace(comSpec) ? "cmd.exe" : comSpec)
            {
                Arguments = "/d /s /c \"" + command + "\"",
            };
        }
        else
        {
            // OUT-OF-SCOPE: the operator's login shell, probed from the host's environment.
            var shell = Environment.GetEnvironmentVariable("SHELL");
            if (string.IsNullOrWhiteSpace(shell))
                shell = "/bin/sh";
            if (setsid is null)
            {
                info = new ProcessStartInfo(shell);
            }
            else
            {
                // The child of this process is never a process-group leader, so setsid runs the shell in a new session
                // without forking: the process started is the shell itself (its exit code, its tree to kill).
                info = new ProcessStartInfo(setsid);
                info.ArgumentList.Add(shell);
            }

            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(command);
        }

        info.UseShellExecute = false;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.CreateNoWindow = true;
        // OUT-OF-SCOPE: the host's working folder, where the operator started the REPL.
        info.WorkingDirectory = Environment.CurrentDirectory;
        info.Environment["PAGER"] = "cat";
        info.Environment["GIT_PAGER"] = "cat";
        return info;
    }

    [SuppressVfsCompliance("OUT-OF-SCOPE: probes PATH for the setsid system binary, not a VFS mount.")]
    private static string? ProbeSetsid()
    {
        if (OperatingSystem.IsWindows())
            return null;

        // OUT-OF-SCOPE: the host's PATH, as the operator's terminal reads it.
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder, "setsid");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
