using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.Principal;
using Orkeon.Compliance.Vfs;
using Orkeon.Tools.Code.Sessions.Windows;

namespace Orkeon.Tools.Code.Sessions;

/// <summary>
/// Confinement by an AppContainer inside a Job Object (<see cref="ShellConfinementMode.AppContainer"/>,
/// Windows 10 and later). Each session gets an AppContainer profile of its own, ACEs for its SID on
/// the mounts of the view (<see cref="AclPlan"/>), a job that kills every process when the session
/// ends or the host dies, and no network unless the view allows it. The commands see the physical
/// paths, as unconfined.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A session first writes its ledger (<see cref="ShellSessionLedger"/>), then grants the ACEs;
/// it takes them back, deletes its profile and its ledger when it ends. A host that crashed leaves
/// ledgers behind: the first session this process opens sweeps those of hosts that are gone
/// (<see cref="SweepStaleSessions(string)"/>).</item>
/// <item>What a normal AppContainer can read without an ACE stays readable: the trees that grant
/// <c>ALL APPLICATION PACKAGES</c> (the Windows directory, Program Files). The user's profile, the other
/// drives' data and the host's temporary folder are out of reach. A program installed under the user's
/// profile is reached through <see cref="ShellConfinementOptions.ReadOnlyPaths"/>.</item>
/// <item><c>TEMP</c>, <c>TMP</c> and <c>HOME</c> point into the profile's own folder, the only place
/// the container writes outside its read-write mounts; a command that names no directory starts
/// there.</item>
/// </list>
/// </remarks>
/// <param name="probe">Whether AppContainer works here; probed once per process by default.</param>
/// <param name="ledgers">The folder of the session ledgers; the current user's by default.</param>
internal sealed class AppContainerShellSandbox(Func<ShellSandboxAvailability> probe, string? ledgers = null) : IShellSandbox
{
    /// <summary>The prefix of every AppContainer profile a session creates; the sweep touches no other.</summary>
    public const string ProfileNamePrefix = "Orkeon.Shell.";

    private static readonly Lazy<ShellSandboxAvailability> s_probe = new(Probe);
    private static int s_swept;

    /// <summary>A sandbox over the process-wide probe and the current user's ledgers.</summary>
    public AppContainerShellSandbox()
        : this(static () => s_probe.Value)
    {
    }

    /// <summary>The instance a tool built without dependency injection uses.</summary>
    public static AppContainerShellSandbox Shared { get; } = new();

    /// <inheritdoc />
    public string Name => "appcontainer";

    /// <inheritdoc />
    public bool UsesVirtualPaths => false;

    /// <inheritdoc />
    public ShellSandboxAvailability Availability => probe();

    /// <summary>
    /// Null when every extra read-only path is a fully qualified host path; otherwise the first one
    /// that is not.
    /// </summary>
    public static string? FirstUnqualifiedPath(IEnumerable<string> readOnlyPaths)
        => readOnlyPaths.FirstOrDefault(p => !Path.IsPathFullyQualified(p));

    /// <inheritdoc />
    public Task<IShellSession> OpenAsync(ShellSessionView view, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        var availability = probe();
        if (!availability.IsAvailable)
            throw new ShellSessionException($"Confinement unavailable: {availability.Reason}.");
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
            throw new ShellSessionException("Confinement unavailable: AppContainer confines a process on Windows 10 and later only.");
        if (FirstUnqualifiedPath(view.ReadOnlyPaths) is { } relative)
            throw new ShellSessionException($"Confinement misconfigured: the read-only path '{relative}' is not a fully qualified path.");

        var store = ledgers is null ? ShellSessionLedgerStore.ForCurrentUser() : new ShellSessionLedgerStore(ledgers);
        return Task.Run<IShellSession>(() =>
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException();
            if (Interlocked.Exchange(ref s_swept, 1) == 0)
                _ = SweepStaleSessions(store.Directory);
            return OpenSession(view, store);
        }, cancellationToken);
    }

    /// <summary>
    /// Takes back what the sessions of dead hosts left in <paramref name="ledgerDirectory"/>: their
    /// ACEs, their AppContainer profiles, their ledgers. A ledger whose host still runs is left alone,
    /// and so is one whose ACEs could not all be removed (the next sweep tries again).
    /// </summary>
    /// <returns>The number of sessions cleaned up.</returns>
    [SupportedOSPlatform("windows")]
    public static int SweepStaleSessions(string ledgerDirectory)
    {
        var store = new ShellSessionLedgerStore(ledgerDirectory);
        var swept = 0;
        foreach (var (profileName, ledger) in store.ReadAll())
        {
            if (ledger is null || !string.Equals(ledger.ProfileName, profileName, StringComparison.Ordinal) || !ledger.IsStale(StartTicksOf))
                continue;

            if (!TakeBack(ledger, store))
                continue;

            if (AppContainerProfile.Delete(ledger.ProfileName))
            {
                store.Delete(ledger.ProfileName);
                swept++;
            }
        }

        return swept;
    }

    /// <summary>The UTC start ticks of the current process, as a ledger records its owner.</summary>
    internal static long CurrentProcessStartTicks()
    {
        using var current = Process.GetCurrentProcess();
        return current.StartTime.ToUniversalTime().Ticks;
    }

    [SupportedOSPlatform("windows")]
    [SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: creates the AppContainer profile's own TEMP folder, host state of the confinement outside every mount; never an agent path.")]
    private static WindowsShellSession OpenSession(ShellSessionView view, ShellSessionLedgerStore store)
    {
        AppContainerProfile profile;
        try
        {
            profile = AppContainerProfile.Create(AppContainerProfile.NewName());
        }
        catch (Win32Exception ex)
        {
            throw new ShellSessionException($"The AppContainer session could not start: {ex.Message}", ex);
        }

        var plan = AclPlan.Build(view.Paths, view.ReadOnlyPaths);
        var isolations = AclPlan.Isolations(view.Paths, view.ReadOnlyPaths);
        var ledger = new ShellSessionLedger
        {
            ProfileName = profile.Name,
            ContainerSid = profile.Sid.Value,
            HostPid = Environment.ProcessId,
            HostStartTicks = CurrentProcessStartTicks(),
            GrantedPaths = [.. AclPlan.PathsOf(plan)],
        };

        try
        {
            // The ledger comes first: a crash between two changes leaves a record of every path. The
            // isolations come before the ACEs, so no grant ever flows into a path it must not reach.
            lock (AclIsolation.Sync)
            {
                ledger = ledger with
                {
                    Isolations = [.. isolations.Select(path => (Path: path, Dacl: OriginalDacl(path, store)))
                        .Where(i => i.Dacl is not null)
                        .Select(i => new ShellSessionIsolation { Path = i.Path, OriginalDacl = i.Dacl! })],
                };
                store.Write(ledger);
                AclIsolation.Apply(ledger.Isolations.Select(i => i.Path), profile.Sid);
            }

            AclGrants.Apply(plan, profile.Sid);

            var environment = ShellEnvironment.RestrictedVariables();
            var folder = profile.FolderPath();
            if (folder is not null)
            {
                var temp = Path.Combine(folder, "Temp");
                Directory.CreateDirectory(temp);
                environment["TEMP"] = temp;
                environment["TMP"] = temp;
                environment["HOME"] = folder;
            }

            SecurityIdentifier[] capabilities = view.AllowNetwork
                ? [AppContainerProfile.InternetClient, AppContainerProfile.PrivateNetworkClientServer]
                : [];
            return new WindowsShellSession(JobObject.Create(), profile.Sid, capabilities, environment, folder, () => Cleanup(ledger, store));
        }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            Cleanup(ledger, store);
            throw new ShellSessionException($"The AppContainer session could not start: {ex.Message}", ex);
        }
    }

    /// <summary>Takes a session's ACEs back, deletes its profile, then its ledger. Never throws.</summary>
    [SupportedOSPlatform("windows")]
    [SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup at the end of a session: whatever is left stays in the ledger, which the next host sweeps.")]
    private static void Cleanup(ShellSessionLedger ledger, ShellSessionLedgerStore store)
    {
        try
        {
            if (TakeBack(ledger, store) && AppContainerProfile.Delete(ledger.ProfileName))
                store.Delete(ledger.ProfileName);
        }
        catch
        {
            // Left for the next sweep.
        }
    }

    /// <summary>
    /// Removes a session's ACEs and writes back the DACLs it isolated. A path another session of the
    /// store also isolated stays as it is: that session still relies on it, and puts it back when it
    /// ends.
    /// </summary>
    /// <returns>True when nothing is left; otherwise the ledger keeps what is (for the next sweep).</returns>
    [SupportedOSPlatform("windows")]
    private static bool TakeBack(ShellSessionLedger ledger, ShellSessionLedgerStore store)
    {
        var failedPaths = AclGrants.Purge(ledger.GrantedPaths, new SecurityIdentifier(ledger.ContainerSid));
        List<ShellSessionIsolation> failedIsolations = [];
        lock (AclIsolation.Sync)
        {
            var shared = IsolationsOfOtherSessions(ledger.ProfileName, store);
            foreach (var isolation in ledger.Isolations)
            {
                if (!shared.ContainsKey(isolation.Path) && !AclIsolation.Restore(isolation.Path, isolation.OriginalDacl))
                    failedIsolations.Add(isolation);
            }

            if (failedPaths.Count == 0 && failedIsolations.Count == 0)
            {
                // Written at once, under the lock: a session opening now must not take a DACL put
                // back for one still isolated.
                if (ledger.Isolations.Count > 0)
                    store.Write(ledger with { Isolations = [] });
                return true;
            }

            store.Write(ledger with { GrantedPaths = [.. failedPaths], Isolations = [.. failedIsolations] });
            return false;
        }
    }

    /// <summary>
    /// The DACL a session about to isolate <paramref name="path"/> must write back: the one another
    /// session recorded when it isolated the path first, or else the path's own; null when the path
    /// does not exist.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string? OriginalDacl(string path, ShellSessionLedgerStore store)
        => IsolationsOfOtherSessions(null, store).TryGetValue(path, out var original)
            ? original
            : AclIsolation.Read(path);

    /// <summary>The isolations the ledgers of the store record, by path, except those of <paramref name="profileName"/>.</summary>
    private static Dictionary<string, string> IsolationsOfOtherSessions(string? profileName, ShellSessionLedgerStore store)
    {
        var isolations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, other) in store.ReadAll())
        {
            if (other is null || string.Equals(name, profileName, StringComparison.Ordinal))
                continue;
            foreach (var isolation in other.Isolations)
                isolations.TryAdd(isolation.Path, isolation.OriginalDacl);
        }

        return isolations;
    }

    /// <summary>The UTC start ticks of the live process <paramref name="pid"/>, or null when there is none.</summary>
    [SuppressMessage("Design", "CA1031", Justification = "A process that exits while it is inspected, or that the host may not inspect, is not the host that wrote the ledger.")]
    private static long? StartTicksOf(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch
        {
            // Alive but out of reach (another user's): not the host that wrote the ledger.
            return long.MinValue;
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Availability probe: any failure creating the profile, the job or the process means AppContainer is unavailable, with the failure as the reason; it is never propagated.")]
    private static ShellSandboxAvailability Probe()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
            return new ShellSandboxAvailability(OperatingSystem.IsWindows()
                ? "AppContainer confinement needs Windows 10 or later"
                : "AppContainer confines a process on Windows only");

        // The profile, the job and the attributes of a real session, around a command that only exits.
        var name = AppContainerProfile.NewName();
        try
        {
            var profile = AppContainerProfile.Create(name);
            var session = new WindowsShellSession(JobObject.Create(), profile.Sid, [], ShellEnvironment.RestrictedVariables(), profile.FolderPath(), null);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var result = session.RunAsync(new ShellInvocation("exit", ["0"], null, TimeSpan.FromSeconds(30), WindowsBuiltin: true), timeout.Token)
                    .GetAwaiter().GetResult();
                return result.ExitCode == 0
                    ? ShellSandboxAvailability.Available
                    : new ShellSandboxAvailability($"a process in an AppContainer exited with {result.ExitCode}: {result.Stderr.Trim()}");
            }
            finally
            {
                session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            return new ShellSandboxAvailability($"an AppContainer could not be started ({ex.Message})");
        }
        finally
        {
            AppContainerProfile.Delete(name);
        }
    }
}
