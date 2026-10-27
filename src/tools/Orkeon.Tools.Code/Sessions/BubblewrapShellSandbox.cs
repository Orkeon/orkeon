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
/// <param name="readEnvironment">Reads one variable of the parent environment; null when it is unset.</param>
/// <param name="kindOf">What a host path is: a directory, a file (a socket counts), or null when absent.</param>
internal sealed class BubblewrapShellSandbox(
    Func<BubblewrapAvailability> probe,
    Func<string, string?> readEnvironment,
    Func<string, ConfinedAccess?> kindOf) : IShellSandbox
{
    /// <summary>A sandbox over the process-wide probe.</summary>
    public BubblewrapShellSandbox()
        : this(BubblewrapProbe.Current)
    {
    }

    /// <summary>A sandbox over <paramref name="probe"/>, the process environment and the host's paths.</summary>
    public BubblewrapShellSandbox(Func<BubblewrapAvailability> probe)
        : this(probe, Environment.GetEnvironmentVariable, BubblewrapProbe.HiddenKindOf)
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

        var startInfo = Prepare();
        startInfo.ArgumentList.Add("-s");
        return await PosixShellSession.StartAsync(
                startInfo, watchedHostPid: null, startOnDedicatedThread: true, hostSeesPrivateDirectory: false, cancellationToken,
                new PosixShellSession.DetachedLaunch(Prepare, view.Paths, OnDedicatedThread: true))
            .ConfigureAwait(false);

        ProcessStartInfo Prepare() => PrepareStartInfo(view, bubblewrap.ExecutablePath!);
    }

    /// <summary>
    /// The start of <c>bwrap &lt;view&gt; -- /bin/sh</c>, without starting it. With the network, the
    /// sockets of the operator's session follow (#216): each socket path that exists and is no
    /// directory is bound read-write at its own path, and their variables are propagated; without the
    /// network, neither, since the credential they lead to only serves a remote. The variables the
    /// operator added pass whatever the network, and no path they name is bound: only a socket needs
    /// the network, and the view stays what the VFS and <c>ReadOnlyPaths</c> say.
    /// </summary>
    internal ProcessStartInfo PrepareStartInfo(ShellSessionView view, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(view);
        List<string> sockets = view.AllowNetwork
            ? [.. ShellEnvironment.SessionSocketPaths(readEnvironment).Where(path => kindOf(path) == ConfinedAccess.HiddenFile)]
            : [];
        var info = new ProcessStartInfo(executablePath);
        foreach (var argument in BubblewrapCommandLine.Build(view.Paths, view.ReadOnlyPaths, view.AllowNetwork, "/", sockets))
            info.ArgumentList.Add(argument);
        info.ArgumentList.Add(UnconfinedShellSandbox.PosixShell);
        ShellEnvironment.ConfigureRestricted(info, readEnvironment, includeSessionSockets: view.AllowNetwork, view.PropagatedVariables);
        return info;
    }
}
