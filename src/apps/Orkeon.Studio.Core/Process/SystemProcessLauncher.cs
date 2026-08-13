using System.ComponentModel;
using System.Diagnostics;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.Process;

/// <summary>
/// The real launcher, on <see cref="System.Diagnostics.Process"/>.
/// <para>
/// Output is delivered through the asynchronous line events (<c>BeginOutputReadLine</c>),
/// so a long run fills the UI's log panel as it goes instead of at the end. Callbacks are
/// serialized on a private lock: stdout and stderr are read on two different threadpool
/// threads, and a UI sink must not have to defend against that.
/// </para>
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application spawning the co-installed orkeon CLI; " +
    "the child's own filesystem access goes through the VFS inside that process, and the " +
    "working directory handled here is a physical path chosen by the user before any mount exists.")]
public sealed class SystemProcessLauncher : IProcessLauncher
{
    /// <summary>
    /// How long we wait for the output pipes to reach end-of-file after the child exited.
    /// A grandchild that inherited the pipes can hold them open; the run must not hang on it.
    /// </summary>
    private static readonly TimeSpan OutputFlushTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Shared stateless instance.</summary>
    public static SystemProcessLauncher Instance { get; } = new();

    /// <inheritdoc />
    public async Task<ProcessRunResult> RunAsync(
        ProcessLaunchRequest request,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = BuildStartInfo(request);

        using var process = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var sink = new SerializedOutputSink(onOutput);
        var stdoutClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
                stdoutClosed.TrySetResult();
            else
                sink.Emit(ProcessOutputChannel.StandardOutput, e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
                stderrClosed.TrySetResult();
            else
                sink.Emit(ProcessOutputChannel.StandardError, e.Data);
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
                return ProcessRunResult.NotStarted($"The operating system refused to start {request.FileName}.");
        }
        catch (Win32Exception ex)
        {
            return ProcessRunResult.NotStarted($"Cannot start {request.FileName}: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return ProcessRunResult.NotStarted($"Cannot start {request.FileName}: {ex.Message}");
        }
        catch (PlatformNotSupportedException ex)
        {
            return ProcessRunResult.NotStarted($"Cannot start {request.FileName}: {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var handle = new SystemProcessHandle(process);
        var termination = ProcessTerminationMode.Exited;
        var cancelled = false;

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            termination = await ProcessTerminator
                .TerminateAsync(handle, request.GracePeriod, CancellationToken.None)
                .ConfigureAwait(false);
            await handle.WaitForExitAsync(OutputFlushTimeout, CancellationToken.None).ConfigureAwait(false);
        }

        stopwatch.Stop();
        await WaitForOutputFlushAsync(stdoutClosed.Task, stderrClosed.Task).ConfigureAwait(false);

        var rawExitCode = ReadExitCode(process);
        return cancelled
            ? ProcessRunResult.FromCancellation(rawExitCode, termination, stopwatch.Elapsed)
            : ProcessRunResult.FromExitCode(rawExitCode, stopwatch.Elapsed);
    }

    private static ProcessStartInfo BuildStartInfo(ProcessLaunchRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // ArgumentList, never a joined command line: a crew path holding a space or a quote
        // must reach the child exactly as the user picked it.
        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            startInfo.WorkingDirectory = request.WorkingDirectory;

        foreach (var (key, value) in request.Environment)
            startInfo.Environment[key] = value;

        return startInfo;
    }

    private static async Task WaitForOutputFlushAsync(Task stdoutClosed, Task stderrClosed)
    {
        var both = Task.WhenAll(stdoutClosed, stderrClosed);
        await Task.WhenAny(both, Task.Delay(OutputFlushTimeout)).ConfigureAwait(false);
    }

    private static int ReadExitCode(System.Diagnostics.Process process)
    {
        try { return process.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }

    /// <summary>Funnels both reader threads through one lock so the sink sees one line at a time.</summary>
    private sealed class SerializedOutputSink(Action<ProcessOutputLine>? onOutput)
    {
        private readonly Lock _gate = new();

        public void Emit(ProcessOutputChannel channel, string text)
        {
            if (onOutput is null)
                return;

            var line = ProcessOutputLine.Now(channel, text);
            lock (_gate)
            {
                onOutput(line);
            }
        }
    }
}
