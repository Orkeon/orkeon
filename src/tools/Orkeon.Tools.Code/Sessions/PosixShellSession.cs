using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>
/// A session over one long-lived POSIX shell reading its commands on its input (<c>sh -s</c>), run on
/// the host or under a confinement that wraps it (bubblewrap). The protocol is
/// <see cref="ShellScript"/>'s: the host writes one quoted fragment per command and reads its frame back.
/// </summary>
/// <remarks>
/// The shell exits when its input closes, and <see cref="DisposeAsync"/> closes it, waits up to two
/// seconds, then kills the whole process tree. A timed-out or cancelled command leaves the session out
/// of step: every later command fails with <see cref="ShellSessionException"/> until the owner disposes
/// it and opens a new one.
/// </remarks>
internal sealed class PosixShellSession : IShellSession
{
    private static readonly TimeSpan s_startTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan s_exitGrace = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_stderrGrace = TimeSpan.FromMilliseconds(500);
    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private const int StderrTailLength = 2_000;

    private readonly Process _process;
    private readonly ManualResetEventSlim? _keeper;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A command may still be queued on the gate when the session ends; a SemaphoreSlim without AvailableWaitHandle holds no unmanaged resource.")]
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StringBuilder _pending = new();
    private readonly char[] _chunk = new char[16 * 1024];
    private readonly StringBuilder _stderrTail = new();
    private readonly Task _stderrPump;
    private readonly object _sync = new();
    private Task? _disposal;
    private volatile bool _broken;
    private bool _removeDirectoryAfterKill;
    private string? _privateDirectory;

    private PosixShellSession(Process process, ManualResetEventSlim? keeper)
    {
        _process = process;
        _keeper = keeper;
        _stdin = process.StandardInput;
        _stdout = process.StandardOutput;
        ProcessId = process.Id;
        _stderrPump = PumpStderrAsync(process.StandardError);
    }

    /// <summary>The process the session started (the shell, or the confinement that wraps it).</summary>
    public int ProcessId { get; }

    /// <summary>The session's private directory, as the shell sees it; null before the shell is ready.</summary>
    internal string? PrivateDirectory => _privateDirectory;

    /// <summary>
    /// Starts <paramref name="startInfo"/> (a shell reading its input, possibly behind a wrapper),
    /// runs the preamble and waits for its ready marker.
    /// </summary>
    /// <param name="startInfo">The process to start; its redirections and encodings are set here.</param>
    /// <param name="watchedHostPid">The host pid a watchdog watches (see <see cref="ShellScript.Preamble"/>), or null.</param>
    /// <param name="startOnDedicatedThread">
    /// Starts the process from a thread that lives as long as the session. A parent-death signal
    /// (<c>PR_SET_PDEATHSIG</c>, which <c>bwrap --die-with-parent</c> sets) fires when the THREAD that
    /// forked the child exits, not the process: started from a pool thread that later retires, the
    /// session would be killed under the agent.
    /// </param>
    /// <param name="hostSeesPrivateDirectory">
    /// Whether the session's private directory is a host path (unconfined, Seatbelt): when the session
    /// had to be killed, its cleanup never ran, and the host removes the directory. A confined view
    /// with its own <c>/tmp</c> (bubblewrap) takes it away by itself.
    /// </param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <exception cref="ShellSessionException">The shell did not become ready.</exception>
    public static async Task<PosixShellSession> StartAsync(
        ProcessStartInfo startInfo, int? watchedHostPid, bool startOnDedicatedThread, bool hostSeesPrivateDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.StandardInputEncoding = s_utf8;
        startInfo.StandardOutputEncoding = s_utf8;
        startInfo.StandardErrorEncoding = s_utf8;

        var (process, keeper) = startOnDedicatedThread
            ? await StartOnDedicatedThreadAsync(startInfo).ConfigureAwait(false)
            : (StartProcess(startInfo), null);

        var session = new PosixShellSession(process, keeper) { _removeDirectoryAfterKill = hostSeesPrivateDirectory };
        try
        {
            await session.HandshakeAsync(watchedHostPid, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ShellRunResult> RunAsync(ShellInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_broken)
                throw new ShellSessionException("The shell session is closed.");

            var nonce = NewNonce();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(invocation.Timeout);
            try
            {
                await WriteAsync(ShellScript.Command(invocation, nonce), timeout.Token).ConfigureAwait(false);
                var frame = await ReadFrameAsync(nonce, timeout.Token).ConfigureAwait(false);
                return new ShellRunResult(frame.ExitCode, frame.Stdout, frame.Stderr);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _broken = true;
                return new ShellRunResult(-1, "", "", TimedOut: true);
            }
            catch (OperationCanceledException)
            {
                _broken = true;
                throw;
            }
            catch (ShellSessionException)
            {
                _broken = true;
                throw;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                _broken = true;
                throw new ShellSessionException(
                    $"The shell session ended while running the command{await StderrNoteAsync().ConfigureAwait(false)}", ex);
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
        lock (_sync)
            _disposal ??= DisposeCoreAsync();
        return new ValueTask(_disposal);
    }

    [SuppressMessage("Design", "CA1031", Justification = "Disposal never throws: each step is best-effort, and the kill that follows covers a step that failed.")]
    private async Task DisposeCoreAsync()
    {
        // Run the steps off the caller's flow: closing the input must not run under the lock.
        await Task.Yield();
        _broken = true;
        try
        {
            _stdin.Close();
        }
        catch (Exception)
        {
            // The shell may already be gone: the kill below covers it.
        }

        if (!await WaitForExitAsync(s_exitGrace).ConfigureAwait(false))
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // It exited between the wait and the kill, or cannot be reached any more.
            }

            await WaitForExitAsync(s_exitGrace).ConfigureAwait(false);
            await RemovePrivateDirectoryAsync().ConfigureAwait(false);
        }

        _keeper?.Set();
        try
        {
            _process.Dispose();
        }
        catch (Exception)
        {
            // Nothing left to release.
        }
    }

    /// <summary>
    /// Removes the private directory of a killed shell, whose own cleanup (its EXIT trap) never ran,
    /// by a process: the directory is the session's scratch space on the host, outside every mount.
    /// </summary>
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup during disposal: a directory left behind is only a few empty files in the temp directory.")]
    private async Task RemovePrivateDirectoryAsync()
    {
        // mktemp -d names an absolute directory; anything else is not removed.
        if (!_removeDirectoryAfterKill || _privateDirectory is not { Length: > 1 } directory || directory[0] != '/')
            return;

        try
        {
            // OUT-OF-SCOPE: the session's own scratch directory on the host, not a VFS path.
            var startInfo = new ProcessStartInfo("/bin/rm") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in (string[])["-rf", "--", directory])
                startInfo.ArgumentList.Add(argument);
            using var remove = Process.Start(startInfo);
            if (remove is not null)
            {
                using var cts = new CancellationTokenSource(s_exitGrace);
                await remove.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Left behind.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A wait during disposal: a failure means the process is gone or unreachable, which the caller handles by killing it.")]
    private async Task<bool> WaitForExitAsync(TimeSpan grace)
    {
        try
        {
            using var cts = new CancellationTokenSource(grace);
            await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task HandshakeAsync(int? watchedHostPid, CancellationToken cancellationToken)
    {
        var nonce = NewNonce();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(s_startTimeout);
        try
        {
            await WriteAsync(ShellScript.Preamble(nonce, watchedHostPid), timeout.Token).ConfigureAwait(false);
            while (true)
            {
                if (ShellScript.TryFindReady(_pending.ToString(), nonce, out var consumed, out var directory))
                {
                    _pending.Remove(0, consumed);
                    _privateDirectory = directory;
                    return;
                }

                await ReadChunkAsync(timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ShellSessionException(
                $"The shell session did not start within {s_startTimeout.TotalSeconds:0} seconds{await StderrNoteAsync().ConfigureAwait(false)}");
        }
        catch (IOException ex)
        {
            throw new ShellSessionException($"The shell session did not start{await StderrNoteAsync().ConfigureAwait(false)}", ex);
        }
    }

    private async Task<ShellFrame> ReadFrameAsync(string nonce, CancellationToken cancellationToken)
    {
        while (true)
        {
            switch (ShellScript.ParseFrame(_pending.ToString(), nonce, out var frame, out var consumed))
            {
                case ShellFrameStatus.Complete:
                    _pending.Remove(0, consumed);
                    return frame!;
                case ShellFrameStatus.Malformed:
                    throw new ShellSessionException("The shell session answered out of step.");
                default:
                    await ReadChunkAsync(cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task ReadChunkAsync(CancellationToken cancellationToken)
    {
        var read = await _stdout.ReadAsync(_chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
        if (read == 0)
            throw new ShellSessionException($"The shell session ended{await StderrNoteAsync().ConfigureAwait(false)}");
        _pending.Append(_chunk, 0, read);
    }

    private async Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        await _stdin.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// How the session's process ended and what its own error output said last (a confinement that
    /// refuses its profile says why there), as a sentence ending.
    /// </summary>
    private async Task<string> StderrNoteAsync()
    {
        // The shell's last words may still be in the pipe when its output closes. The pump never
        // faults; a wait that times out leaves it running, with no timer behind.
        try
        {
            await _stderrPump.WaitAsync(s_stderrGrace).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // What came within the grace is what is reported.
        }

        var status = await WaitForExitAsync(s_stderrGrace).ConfigureAwait(false)
            ? $" (exit code {ExitCodeOrUnknown()})"
            : "";
        string tail;
        lock (_stderrTail)
            tail = _stderrTail.ToString().Trim();
        return tail.Length == 0 ? $"{status}, with nothing on its error output." : $"{status}: {tail}";
    }

    [SuppressMessage("Design", "CA1031", Justification = "A diagnostic: a process already disposed has no exit code to report.")]
    private string ExitCodeOrUnknown()
    {
        try
        {
            return _process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A background drain of the session's error output: a failed read only ends the drain.")]
    private async Task PumpStderrAsync(StreamReader stderr)
    {
        var buffer = new char[1024];
        try
        {
            int read;
            while ((read = await stderr.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
            {
                lock (_stderrTail)
                {
                    _stderrTail.Append(buffer, 0, read);
                    if (_stderrTail.Length > StderrTailLength)
                        _stderrTail.Remove(0, _stderrTail.Length - StderrTailLength);
                }
            }
        }
        catch (Exception)
        {
            // The process is gone or disposed.
        }
    }

    private static string NewNonce() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    private static Process StartProcess(ProcessStartInfo startInfo)
        => Process.Start(startInfo) ?? throw new ShellSessionException("The shell session could not be started.");

    [SuppressMessage("Design", "CA1031", Justification = "Any failure to start is handed to the awaiting caller through the task.")]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The keeper is handed to the session, which sets it when it ends; the thread disposes it after.")]
    private static Task<(Process Process, ManualResetEventSlim? Keeper)> StartOnDedicatedThreadAsync(ProcessStartInfo startInfo)
    {
        var started = new TaskCompletionSource<(Process, ManualResetEventSlim?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var keeper = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            Process process;
            try
            {
                process = StartProcess(startInfo);
            }
            catch (Exception ex)
            {
                keeper.Dispose();
                started.SetException(ex);
                return;
            }

            started.SetResult((process, keeper));
            keeper.Wait();
            keeper.Dispose();
        })
        {
            IsBackground = true,
            Name = "Orkeon shell session",
        };
        thread.Start();
        return started.Task;
    }
}
