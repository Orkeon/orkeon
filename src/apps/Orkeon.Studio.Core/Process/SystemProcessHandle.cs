using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Orkeon.Studio.Core.Process;

/// <summary>
/// <see cref="IProcessHandle"/> over a real <see cref="System.Diagnostics.Process"/>.
/// <para>
/// On Unix the graceful stop is a genuine <c>SIGINT</c> sent with <c>kill(2)</c> — exactly
/// what Ctrl+C delivers in a terminal, which is the signal the CLI handles.
/// </para>
/// <para>
/// On Windows there is no per-process equivalent: <c>GenerateConsoleCtrlEvent</c> only
/// addresses a process group attached to a console, and Studio's WPF front-end has no
/// console at all while its TUI front-ends would signal themselves along with the child.
/// <c>CloseMainWindow</c> is not an option either — it posts WM_CLOSE, which a console
/// application never receives. Windows therefore goes straight to the kill step; the run is
/// still reported as interrupted (130), only the child loses its own unwind.
/// </para>
/// </summary>
public sealed class SystemProcessHandle : IProcessHandle
{
    private const int Sigint = 2;

    private readonly System.Diagnostics.Process _process;

    /// <summary>Wraps an already started process.</summary>
    public SystemProcessHandle(System.Diagnostics.Process process)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
    }

    /// <inheritdoc />
    public int ProcessId
    {
        get
        {
            try { return _process.Id; }
            catch (InvalidOperationException) { return -1; }
        }
    }

    /// <inheritdoc />
    public bool HasExited
    {
        get
        {
            try { return _process.HasExited; }
            catch (InvalidOperationException) { return true; }
        }
    }

    /// <inheritdoc />
    public bool TryRequestGracefulStop(out string? failureReason)
    {
        if (OperatingSystem.IsWindows())
        {
            failureReason = "Windows offers no per-child Ctrl+C that would not also signal Studio itself; " +
                            "the process is stopped by killing its tree.";
            return false;
        }

        var pid = ProcessId;
        if (pid <= 0)
        {
            failureReason = "The process has no addressable id (it already exited).";
            return false;
        }

        if (KillNative(pid, Sigint) == 0)
        {
            failureReason = null;
            return true;
        }

        failureReason = $"kill(SIGINT) on pid {pid} failed with errno {Marshal.GetLastPInvokeError()}.";
        return false;
    }

    /// <inheritdoc />
    public void Kill()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Exited between the check and the kill — the caller wanted it dead, it is.
        }
        catch (NotSupportedException)
        {
            // No tree kill on this platform for this process; fall back to the process itself.
            try { _process.Kill(); } catch (InvalidOperationException) { /* already gone */ }
        }
        catch (Win32Exception)
        {
            // Access denied or already reaped; nothing more we can do from here.
        }
    }

    /// <inheritdoc />
    public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (HasExited)
            return true;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout > TimeSpan.Zero)
            timeoutCts.CancelAfter(timeout);

        try
        {
            await _process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return HasExited;
        }
    }

    // DllImport rather than the source-generated LibraryImport: the latter emits unsafe code,
    // and this project does not enable it for one blittable two-int call.
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int KillNative(int pid, int signal);
}
