using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Orkeon.Compliance.Vfs;
using Orkeon.Tools.Code.Confinement;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>
/// Confinement by Seatbelt (<see cref="ShellConfinementMode.Seatbelt"/>, macOS): the session is
/// <c>/usr/bin/sandbox-exec -p &lt;profile&gt; /bin/sh -s</c>, under the profile
/// <see cref="SeatbeltProfile"/> builds from the VFS. Seatbelt filters what the process may touch but
/// does not move anything: the session sees the PHYSICAL paths, and the arguments and working
/// directory are translated as for the unconfined session.
/// </summary>
/// <remarks>
/// <para>
/// Seatbelt matches the path a link leads to, so every path of the profile is canonicalized first
/// (<see cref="SeatbeltProbe.Canonicalize(string)"/>: <c>/var/folders</c> is <c>/private/var/folders</c>).
/// </para>
/// <para>
/// Each session gets a scratch directory of its own on the host, the only writable place outside the
/// mounts, set as its <c>HOME</c> and <c>TMPDIR</c> (the counterpart of bubblewrap's private
/// <c>/tmp</c>); it is removed when the session ends.
/// </para>
/// <para>
/// macOS has no parent-death signal. The session is started by a launcher shell that first starts the
/// watchdog of <see cref="ShellScript.WatchdogScript"/>, OUTSIDE the sandbox, then becomes
/// <c>sandbox-exec</c> (<c>exec</c>, same pid): the watchdog must be able to probe the host's pid and to
/// kill the session's tree, which a process in the sandbox may not (it signals its own sandbox only).
/// When the host disappears, the watchdog kills the session's tree and removes the scratch directory.
/// </para>
/// </remarks>
/// <param name="probe">Whether Seatbelt works here; probed once per process by default.</param>
internal sealed class SeatbeltShellSandbox(Func<ShellSandboxAvailability> probe) : IShellSandbox
{
    /// <summary>
    /// The launcher, run as <c>/bin/sh -c '&lt;launcher&gt;' _ork_seatbelt &lt;watchdog script&gt;
    /// &lt;host pid&gt; &lt;profile&gt; &lt;scratch directory&gt;</c>: it starts the watchdog on
    /// itself (<c>$$</c>, which stays the session's pid across the <c>exec</c>), then becomes the
    /// confined shell. Every value is an argument, never part of the script.
    /// </summary>
    internal const string Launcher =
        "/bin/sh -c \"$1\" " + ShellScript.WatchdogName + " \"$2\" $$ \"$4\" </dev/null >/dev/null 2>&1 &\n"
        + "exec " + SeatbeltProbe.SandboxExec + " -p \"$3\" " + UnconfinedShellSandbox.PosixShell + " -s\n";

    /// <summary>The name the launcher runs under (its <c>$0</c>).</summary>
    internal const string LauncherName = "_ork_seatbelt";

    /// <summary>A sandbox over the process-wide probe.</summary>
    public SeatbeltShellSandbox()
        : this(SeatbeltProbe.Current)
    {
    }

    /// <summary>The pid the watchdog watches; the host's own unless a test stands a process in for it.</summary>
    internal int WatchedHostPid { get; init; } = Environment.ProcessId;

    /// <inheritdoc />
    public string Name => "seatbelt";

    /// <inheritdoc />
    public bool UsesVirtualPaths => false;

    /// <inheritdoc />
    public ShellSandboxAvailability Availability => probe();

    /// <inheritdoc />
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The session is handed to the ScratchSession returned, which disposes it when it ends.")]
    public async Task<IShellSession> OpenAsync(ShellSessionView view, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        var availability = probe();
        if (!availability.IsAvailable)
            throw new ShellSessionException($"Confinement unavailable: {availability.Reason}.");

        var scratch = CreateScratchDirectory();
        try
        {
            string profile;
            try
            {
                profile = BuildProfile(view, scratch);
            }
            catch (ArgumentException ex)
            {
                throw new ShellSessionException($"Confinement misconfigured: {ex.Message}");
            }

            // The shell starts in its scratch directory: the host's own directory may lie outside the
            // profile, where even resolving it is refused.
            var startInfo = new ProcessStartInfo(UnconfinedShellSandbox.PosixShell) { WorkingDirectory = scratch };
            foreach (var argument in (string[])
                     ["-c", Launcher, LauncherName, ShellScript.WatchdogScript,
                      WatchedHostPid.ToString(CultureInfo.InvariantCulture), profile, scratch])
                startInfo.ArgumentList.Add(argument);
            ShellEnvironment.ConfigureRestricted(startInfo);
            // The host's HOME and TMPDIR lie outside the profile: the scratch directory stands in.
            startInfo.Environment["HOME"] = scratch;
            startInfo.Environment["TMPDIR"] = scratch;

            // The watchdog is the launcher's, so the preamble starts none.
            var session = await PosixShellSession.StartAsync(startInfo, watchedHostPid: null, startOnDedicatedThread: false, hostSeesPrivateDirectory: true, cancellationToken)
                .ConfigureAwait(false);
            return new ScratchSession(session, scratch);
        }
        catch
        {
            RemoveScratchDirectory(scratch);
            throw;
        }
    }

    /// <summary>
    /// The profile of <paramref name="view"/>, every path canonicalized, plus the session's scratch
    /// directory read-write.
    /// </summary>
    internal static string BuildProfile(ShellSessionView view, string scratch)
    {
        var paths = view.Paths
            .Select(path => path with { Source = SeatbeltProbe.Canonicalize(path.Source) })
            .Append(new ConfinedPath(scratch, scratch, ConfinedAccess.ReadWrite));
        var readOnly = view.ReadOnlyPaths.Select(SeatbeltProbe.Canonicalize);
        return SeatbeltProfile.Build(paths, readOnly, view.AllowNetwork);
    }

    [SuppressVfsCompliance("OUT-OF-SCOPE: the session's own scratch directory on the host (its HOME and TMPDIR), outside every mount; the confined process, not the framework, writes there.")]
    private static string CreateScratchDirectory()
    {
        // OUT-OF-SCOPE: host scratch space of the confined session, not a VFS path.
        try
        {
            return SeatbeltProbe.Canonicalize(Directory.CreateTempSubdirectory("orkeon-seatbelt-").FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ShellSessionException($"The shell session's scratch directory could not be created ({ex.Message}).");
        }
    }

    [SuppressVfsCompliance("OUT-OF-SCOPE: removes the session's own scratch directory on the host, outside every mount.")]
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a scratch directory left behind is only temporary files in the host's temp directory.")]
    private static void RemoveScratchDirectory(string scratch)
    {
        // OUT-OF-SCOPE: host scratch space of the confined session, not a VFS path.
        try
        {
            if (Directory.Exists(scratch))
                Directory.Delete(scratch, recursive: true);
        }
        catch (Exception)
        {
            // Left behind.
        }
    }

    /// <summary>A session that removes its scratch directory once its shell has ended.</summary>
    private sealed class ScratchSession(IShellSession inner, string scratch) : IShellSession
    {
        private readonly Lock _sync = new();
        private Task? _disposal;

        public Task<ShellRunResult> RunAsync(ShellInvocation invocation, CancellationToken cancellationToken)
            => inner.RunAsync(invocation, cancellationToken);

        public ValueTask DisposeAsync()
        {
            lock (_sync)
                _disposal ??= DisposeCoreAsync();
            return new ValueTask(_disposal);
        }

        private async Task DisposeCoreAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            RemoveScratchDirectory(scratch);
        }
    }
}
