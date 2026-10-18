using System.Diagnostics;
using Orkeon.Tools.Code.Confinement;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>
/// Confinement by bubblewrap (<see cref="ShellConfinementMode.Bubblewrap"/>, Linux): the session is
/// <c>bwrap &lt;view&gt; -- /bin/sh -s</c>, in the view <see cref="BubblewrapCommandLine"/> builds from
/// the VFS, and sees the virtual paths.
/// </summary>
/// <remarks>
/// <c>--die-with-parent</c> and the PID namespace (bwrap is its init) take the whole tree down when the
/// host dies; the session is started from a thread that lives as long as it does, since the
/// parent-death signal follows the forking thread. The shell also exits when its input closes.
/// </remarks>
/// <param name="probe">Whether bubblewrap works here; probed once per process by default.</param>
internal sealed class BubblewrapShellSandbox(Func<BubblewrapAvailability> probe) : IShellSandbox
{
    /// <summary>A sandbox over the process-wide probe.</summary>
    public BubblewrapShellSandbox()
        : this(BubblewrapProbe.Current)
    {
    }

    /// <inheritdoc />
    public string Name => "bubblewrap";

    /// <inheritdoc />
    public bool UsesVirtualPaths => true;

    /// <inheritdoc />
    public ShellSandboxAvailability Availability
    {
        get
        {
            var bubblewrap = probe();
            return bubblewrap.IsAvailable ? ShellSandboxAvailability.Available : new ShellSandboxAvailability(bubblewrap.Reason ?? "bubblewrap is unavailable");
        }
    }

    /// <inheritdoc />
    public async Task<IShellSession> OpenAsync(ShellSessionView view, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        var bubblewrap = probe();
        if (!bubblewrap.IsAvailable)
            throw new ShellSessionException($"Confinement unavailable: {bubblewrap.Reason}.");

        var startInfo = new ProcessStartInfo(bubblewrap.ExecutablePath!);
        foreach (var argument in BubblewrapCommandLine.Build(view.Paths, view.ReadOnlyPaths, view.AllowNetwork, "/"))
            startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add(UnconfinedShellSandbox.PosixShell);
        startInfo.ArgumentList.Add("-s");
        ShellEnvironment.ConfigureRestricted(startInfo);
        return await PosixShellSession.StartAsync(startInfo, watchedHostPid: null, startOnDedicatedThread: true, hostSeesPrivateDirectory: false, cancellationToken)
            .ConfigureAwait(false);
    }
}
