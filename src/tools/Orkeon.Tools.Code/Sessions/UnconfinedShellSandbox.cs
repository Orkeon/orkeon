using System.Diagnostics;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>
/// No confinement (<see cref="ShellConfinementMode.None"/>): the session runs on the host, as the
/// user of the host, and sees the physical paths. On Linux and macOS it is one <c>/bin/sh -s</c> per
/// agent run with a watchdog that kills its process tree when the host disappears; on Windows each
/// command is its own process, created inside a Job Object the session holds
/// (<see cref="Windows.WindowsShellSession"/>), so nothing outlives the session or the host there either.
/// </summary>
internal sealed class UnconfinedShellSandbox : IShellSandbox
{
    /// <summary>The POSIX shell the session runs.</summary>
    public const string PosixShell = "/bin/sh";

    /// <summary>The instance a tool built without dependency injection uses.</summary>
    public static UnconfinedShellSandbox Shared { get; } = new();

    /// <summary>The pid the watchdog watches; the host's own unless a test stands a process in for it.</summary>
    internal int WatchedHostPid { get; init; } = Environment.ProcessId;

    /// <inheritdoc />
    public string Name => "unconfined";

    /// <inheritdoc />
    public bool UsesVirtualPaths => false;

    /// <inheritdoc />
    public ShellSandboxAvailability Availability => ShellSandboxAvailability.Available;

    /// <inheritdoc />
    public async Task<IShellSession> OpenAsync(ShellSessionView view, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (OperatingSystem.IsWindows())
            return Windows.WindowsShellSession.OpenUnconfined(view.PropagatedVariables);

        var startInfo = new ProcessStartInfo(PosixShell);
        startInfo.ArgumentList.Add("-s");
        ShellEnvironment.ConfigureRestricted(startInfo, ShellSessionSockets.All, view.PropagatedVariables);
        return await PosixShellSession.StartAsync(startInfo, WatchedHostPid, startOnDedicatedThread: false, hostSeesPrivateDirectory: true, cancellationToken, new PosixShellSession.DetachedLaunch(Prepare))
            .ConfigureAwait(false);

        ProcessStartInfo Prepare()
        {
            var info = new ProcessStartInfo(PosixShell);
            ShellEnvironment.ConfigureRestricted(info, ShellSessionSockets.All, view.PropagatedVariables);
            return info;
        }
    }
}
