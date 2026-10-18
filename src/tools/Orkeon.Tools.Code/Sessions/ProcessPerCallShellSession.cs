using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>
/// The session of a host without a POSIX shell (Windows, unconfined): each command is its own process,
/// started directly with its arguments one by one, as the tool always did there. Nothing persists
/// between commands; disposing the session kills the tree of the command still running, if any.
/// </summary>
internal sealed class ProcessPerCallShellSession : IShellSession
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A command may still be queued on the gate when the session ends; a SemaphoreSlim without AvailableWaitHandle holds no unmanaged resource.")]
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A reference to the process RunAsync owns and disposes; DisposeAsync only kills it.")]
    private Process? _running;
    private bool _disposed;

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort read after a timeout kill: failures while draining the killed process's output must not mask the timeout result.")]
    public async Task<ShellRunResult> RunAsync(ShellInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                // NOSONAR - a bare, allowlisted executable name, resolved on the restricted PATH by design.
                FileName = invocation.Executable,
                WorkingDirectory = invocation.WorkingDirectory ?? "",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            ShellEnvironment.ConfigureRestricted(startInfo);
            foreach (var argument in invocation.Arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = startInfo };
            lock (_sync)
            {
                if (_disposed)
                    throw new ShellSessionException("The shell session is closed.");
                process.Start();
                _running = process;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(invocation.Timeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);
                return new ShellRunResult(process.ExitCode, stdout, stderr);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                KillTree(process);
                var stdout = "";
                var stderr = "";
                try
                {
                    stdout = await ReadRestAsync(process.StandardOutput).ConfigureAwait(false);
                    stderr = await ReadRestAsync(process.StandardError).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore read errors after the kill.
                }

                return new ShellRunResult(-1, stdout, stderr, TimedOut: true);
            }
            finally
            {
                // Whatever ends the call - the caller's cancellation included - the tree must not outlive it.
                KillTree(process);
                lock (_sync)
                    _running = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Process? running;
        lock (_sync)
        {
            _disposed = true;
            running = _running;
        }

        if (running is not null)
            KillTree(running);
        return ValueTask.CompletedTask;
    }

    [SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: the process may have exited or be disposed already; a failed kill must not replace the call's outcome.")]
    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already exited, disposed, or out of reach.
        }
    }

    private static async Task<string> ReadRestAsync(StreamReader reader)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        return await reader.ReadToEndAsync(cts.Token).ConfigureAwait(false);
    }
}
