using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Tools.Code.Sessions.Windows;

/// <summary>
/// The shell session of a Windows host: each command is its own process, started directly with its
/// arguments (no shell in between, <see cref="WindowsCommandLine"/>), inside the session's
/// <see cref="JobObject"/>, and inside its AppContainer when the session is confined. A cmd.exe
/// built-in (<see cref="ShellInvocation.WindowsBuiltin"/>) runs through
/// <see cref="WindowsCommandLine.CmdBuiltin"/>, in the same job and container.
/// </summary>
/// <remarks>
/// Nothing persists between commands but the job: a background process a command left keeps
/// running until the session ends. A timeout or the caller's cancellation kills every process of the
/// job and leaves the session unusable (the manager opens a new one). Disposing the session kills the
/// job, then runs the cleanup of its confinement (the ACEs, the profile, the ledger) in the
/// background: the end of a run does not wait for it.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsShellSession : IShellSession
{
    /// <summary>The most characters kept of each stream; the tool truncates far below that.</summary>
    internal const int MaxCapturedLength = 1_000_000;

    private static readonly TimeSpan s_outputGrace = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly JobObject _job;
    private readonly SecurityIdentifier? _containerSid;
    private readonly IReadOnlyList<SecurityIdentifier> _capabilities;
    private readonly string _environmentBlock;
    private readonly string? _path;
    private readonly string? _defaultDirectory;
    private readonly Action? _cleanup;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "A command may still be queued on the gate when the session ends; a SemaphoreSlim without AvailableWaitHandle holds no unmanaged resource.")]
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _sync = new();
    private Task? _disposal;
    private bool _broken;

    /// <summary>A session over <paramref name="job"/>, confined when <paramref name="containerSid"/> is given.</summary>
    /// <param name="job">The job every process is created in; the session owns it.</param>
    /// <param name="containerSid">The AppContainer, or null to run as the host's user.</param>
    /// <param name="capabilities">The capabilities of the AppContainer.</param>
    /// <param name="environment">The environment of every command.</param>
    /// <param name="defaultDirectory">The directory of a command that names none; null keeps the host's.</param>
    /// <param name="cleanup">Undoes the confinement once the job is dead; it must not throw.</param>
    public WindowsShellSession(
        JobObject job,
        SecurityIdentifier? containerSid,
        IReadOnlyList<SecurityIdentifier> capabilities,
        IReadOnlyDictionary<string, string> environment,
        string? defaultDirectory,
        Action? cleanup)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(environment);
        _job = job;
        _containerSid = containerSid;
        _capabilities = capabilities;
        _environmentBlock = WindowsEnvironmentBlock.Build(environment);
        _path = environment.TryGetValue("PATH", out var path) ? path : null;
        _defaultDirectory = defaultDirectory;
        _cleanup = cleanup;
    }

    /// <summary>An unconfined session: the host's user, the restricted environment, a job of its own.</summary>
    public static WindowsShellSession OpenUnconfined()
        => new(JobObject.Create(), null, [], ShellEnvironment.RestrictedVariables(), null, null);

    /// <summary>The session's job (the tests read its processes).</summary>
    internal JobObject Job => _job;

    /// <inheritdoc />
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The process and the log are disposed by the watcher when the job ends.")]
    [SuppressMessage("Reliability", "CA2025", Justification = "The watcher awaits the pumps and then disposes the process, the log and the job object itself.")]
    public async Task<ShellJobHandle> StartDetachedAsync(ShellInvocation invocation, string physicalLogPath, Func<CancellationToken, Task<Stream>> openLogAsync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalLogPath);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_disposal is not null)
                throw new ShellSessionException("The shell session is closed.");
        }

        string applicationPath;
        string commandLine;
        try
        {
            (applicationPath, commandLine) = CommandLineOf(invocation);
        }
        catch (ArgumentException ex)
        {
            throw new ShellSessionException(ex.Message, ex);
        }

        var directory = string.IsNullOrEmpty(invocation.WorkingDirectory) ? _defaultDirectory : invocation.WorkingDirectory;

        // The log is opened through the VFS before anything runs: a failure here leaves no process behind.
        var log = await openLogAsync(cancellationToken).ConfigureAwait(false);

        // A job object of its own: a reset or a timeout of the session's job never kills a detached job.
        var jobObject = JobObject.Create();
        WindowsLaunchedProcess process;
        try
        {
            process = WindowsProcessLauncher.Start(new WindowsLaunch(
                applicationPath, commandLine, _environmentBlock, directory, jobObject, _containerSid, _capabilities));
        }
        catch (ObjectDisposedException ex)
        {
            jobObject.Dispose();
            await log.DisposeAsync().ConfigureAwait(false);
            throw new ShellSessionException("The shell session is closed.", ex);
        }
        catch
        {
            jobObject.Dispose();
            await log.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var logGate = new SemaphoreSlim(1, 1);
        var readers = new CancellationTokenSource();
        var tail = new LiveTailBuffer();
        var pumps = Task.WhenAll(CopyAsync(process.Stdout, log, logGate, tail, readers.Token), CopyAsync(process.Stderr, log, logGate, tail, readers.Token));
        return new ShellJobHandle(
            Path.GetFileNameWithoutExtension(physicalLogPath),
            WatchAsync(process, pumps, readers, log, logGate, jobObject),
            () => Kill(process, jobObject))
        {
            // The log is held open for writing: a reader gets the recent output from memory.
            LiveTail = () => tail.Snapshot(),
        };
    }

    private static async Task<int> WatchAsync(
        WindowsLaunchedProcess process, Task pumps, CancellationTokenSource readers, Stream log, SemaphoreSlim logGate, JobObject jobObject)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

            // A grandchild may still hold the pipes: what came within the grace is what the job printed.
            readers.CancelAfter(s_outputGrace);
            await pumps.ConfigureAwait(false);
            return process.ExitCode ?? -1;
        }
        finally
        {
            await log.DisposeAsync().ConfigureAwait(false);
            logGate.Dispose();
            readers.Dispose();
            process.Dispose();
            jobObject.Dispose();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A pipe broken or cancelled ends the copy; what was read so far is in the log.")]
    private static async Task CopyAsync(Stream source, Stream log, SemaphoreSlim logGate, LiveTailBuffer tail, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                tail.Append(buffer.AsSpan(0, read));
                await logGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await log.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    await log.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    logGate.Release();
                }
            }
        }
        catch
        {
            // Cancelled after the grace, or the pipe broke: the copy ends here.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "The job may have exited, or been disposed, between the check and the kill.")]
    private static void Kill(WindowsLaunchedProcess process, JobObject jobObject)
    {
        try
        {
            _ = NativeMethods.TerminateProcess(process.Handle, 1);
            jobObject.Dispose();
        }
        catch
        {
            // Already gone.
        }
    }

    /// <inheritdoc />
    public async Task<ShellRunResult> RunAsync(ShellInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_disposal is not null)
                    throw new ShellSessionException("The shell session is closed.");
                if (_broken)
                    throw new ShellSessionException("The shell session was reset after a timeout or a cancellation.");
            }

            string applicationPath;
            string commandLine;
            try
            {
                (applicationPath, commandLine) = CommandLineOf(invocation);
            }
            catch (ArgumentException ex)
            {
                // An argument of a built-in or a batch file cmd.exe would expand: the command does not run.
                return new ShellRunResult(1, "", ex.Message);
            }

            var directory = string.IsNullOrEmpty(invocation.WorkingDirectory) ? _defaultDirectory : invocation.WorkingDirectory;
            WindowsLaunchedProcess launched;
            try
            {
                launched = WindowsProcessLauncher.Start(new WindowsLaunch(
                    applicationPath, commandLine, _environmentBlock, directory, _job, _containerSid, _capabilities));
            }
            catch (ObjectDisposedException ex)
            {
                // The session ended (its job closed) while this command was starting.
                throw new ShellSessionException("The shell session is closed.", ex);
            }

            using var process = launched;
            return await WaitAsync(process, invocation.Timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Task disposal;
        lock (_sync)
        {
            if (_disposal is null)
            {
                // The job dies now, whatever the caller does with the returned task.
                _job.Dispose();
                _disposal = _cleanup is null ? Task.CompletedTask : Task.Run(_cleanup);
            }

            disposal = _disposal;
        }

        return new ValueTask(disposal);
    }

    private async Task<ShellRunResult> WaitAsync(WindowsLaunchedProcess process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var readers = new CancellationTokenSource();
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutTask = PumpAsync(process.Stdout, stdout, readers.Token);
        var stderrTask = PumpAsync(process.Stderr, stderr, readers.Token);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The caller's cancellation or the timeout: the whole job dies, the session is spent.
            _job.Terminate();
            lock (_sync)
                _broken = true;
            if (cancellationToken.IsCancellationRequested)
            {
                await readers.CancelAsync().ConfigureAwait(false);
                throw;
            }

            timedOut = true;
        }

        // A grandchild may still hold the pipes: what came within the grace is what the command printed.
        readers.CancelAfter(s_outputGrace);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        string stdoutText, stderrText;
        lock (stdout)
            stdoutText = stdout.ToString();
        lock (stderr)
            stderrText = stderr.ToString();

        return timedOut
            ? new ShellRunResult(-1, stdoutText, stderrText, TimedOut: true)
            : new ShellRunResult(process.ExitCode ?? -1, stdoutText, stderrText);
    }

    [SuppressMessage("Design", "CA1031", Justification = "A pipe broken or cancelled ends the capture; what was read so far is the output.")]
    private static async Task PumpAsync(Stream stream, StringBuilder target, CancellationToken cancellationToken)
    {
        var decoder = s_utf8.GetDecoder();
        var bytes = new byte[16 * 1024];
        var chars = new char[s_utf8.GetMaxCharCount(bytes.Length)];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
                var count = decoder.GetChars(bytes, 0, read, chars, 0, flush: read == 0);
                lock (target)
                {
                    var room = MaxCapturedLength - target.Length;
                    if (room > 0)
                        target.Append(chars, 0, Math.Min(room, count));
                }

                if (read == 0)
                    return;
            }
        }
        catch
        {
            // Cancelled after the grace, or the pipe broke: the capture ends here.
        }
    }

    [SuppressVfsCompliance("OUT-OF-SCOPE: looks up the program of an allowlisted bare command name on the host's PATH, as Process.Start would; not a VFS path.")]
    private (string ApplicationPath, string CommandLine) CommandLineOf(ShellInvocation invocation)
    {
        if (invocation.WindowsBuiltin)
        {
            var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            return (cmd, WindowsCommandLine.CmdBuiltin(cmd, invocation.Executable, invocation.Arguments));
        }

        // OUT-OF-SCOPE: host lookup of the program on PATH. A batch file runs only unconfined.
        var program = WindowsExecutableResolver.Resolve(
                invocation.Executable, _path, File.Exists, out var error, allowBatchFiles: _containerSid is null)
            ?? throw new Win32Exception(2, $"An error occurred trying to start process '{invocation.Executable}': {error}.");
        if (WindowsExecutableResolver.IsBatchFile(program))
        {
            var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            return (cmd, WindowsCommandLine.BatchFile(cmd, program, invocation.Arguments));
        }

        return (program, WindowsCommandLine.Build(program, invocation.Arguments));
    }
}
