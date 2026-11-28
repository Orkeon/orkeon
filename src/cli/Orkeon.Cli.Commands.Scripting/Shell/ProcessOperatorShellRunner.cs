using System.Diagnostics;

namespace Orkeon.Cli.Commands.Scripting.Shell;

/// <summary>
/// Runs a command in the operator's own shell, as their terminal would: <c>$SHELL -c</c> (<c>/bin/sh</c> when unset),
/// <c>%ComSpec% /c</c> on Windows, in the host's working folder, with the host's whole environment plus
/// <c>PAGER=cat</c> and <c>GIT_PAGER=cat</c>. Its standard input is closed at once, so a command that waits for the
/// keyboard reads the end of its input — it fails or meets the timeout, and never takes the console.
/// </summary>
internal sealed class ProcessOperatorShellRunner : IOperatorShellRunner
{
    /// <summary>How long the streams are drained after the process tree was killed.</summary>
    private static readonly TimeSpan s_drainAfterKill = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<OperatorShellRun> RunAsync(
        string command, Action<string> onLine, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(onLine);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process { StartInfo = StartInfo(command) };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        process.Start();
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            return new OperatorShellRun(process.ExitCode, TimedOut: false);
        }
        catch (OperationCanceledException)
        {
            Stop(process);
            cancellationToken.ThrowIfCancellationRequested();
            return new OperatorShellRun(null, TimedOut: true);
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

    private static ProcessStartInfo StartInfo(string command)
    {
        ProcessStartInfo info;
        if (OperatingSystem.IsWindows())
        {
            // OUT-OF-SCOPE: the operator's command interpreter, probed from the host's environment.
            var comSpec = Environment.GetEnvironmentVariable("ComSpec");
            info = new ProcessStartInfo(string.IsNullOrWhiteSpace(comSpec) ? "cmd.exe" : comSpec)
            {
                // cmd.exe reads the rest of its line as the command: no quoting of ours.
                Arguments = "/c " + command,
            };
        }
        else
        {
            // OUT-OF-SCOPE: the operator's login shell, probed from the host's environment.
            var shell = Environment.GetEnvironmentVariable("SHELL");
            info = new ProcessStartInfo(string.IsNullOrWhiteSpace(shell) ? "/bin/sh" : shell);
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
}
