using System.Diagnostics;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Tools.Code.Confinement;

/// <summary>Whether bubblewrap can confine a process on this host: its path, or why not.</summary>
/// <param name="ExecutablePath">The absolute path of <c>bwrap</c> when it works, else null.</param>
/// <param name="Reason">Why confinement is unavailable, else null.</param>
internal sealed record BubblewrapAvailability(string? ExecutablePath, string? Reason)
{
    /// <summary>True when <see cref="ExecutablePath"/> confined a process at the probe.</summary>
    public bool IsAvailable => Reason is null && ExecutablePath is not null;

    /// <summary>Bubblewrap works, at <paramref name="executablePath"/>.</summary>
    public static BubblewrapAvailability Available(string executablePath) => new(executablePath, null);

    /// <summary>Bubblewrap cannot confine a process here, for <paramref name="reason"/>.</summary>
    public static BubblewrapAvailability Unavailable(string reason) => new(null, reason);
}

/// <summary>
/// Finds <c>bwrap</c> on the host and checks once per process that it can create the namespaces the
/// confinement uses: an old kernel, a container without user namespaces or a hardened sysctl refuses
/// them, and a confined call must then fail rather than run unconfined.
/// </summary>
internal static class BubblewrapProbe
{
    private static readonly TimeSpan s_probeTimeout = TimeSpan.FromSeconds(10);

    // Fixed places first, PATH after: an absolute path keeps a binary planted ahead of the system's
    // on PATH from being the one that builds the confinement (S4036).
    private static readonly string[] s_knownPaths = ["/usr/bin/bwrap", "/usr/local/bin/bwrap", "/bin/bwrap"];

    private static readonly Lazy<BubblewrapAvailability> s_current = new(Probe);

    /// <summary>The availability probed at the first call of the process, then remembered.</summary>
    public static BubblewrapAvailability Current() => s_current.Value;

    /// <summary>
    /// What a path the VFS grants no right on is, so the confinement can cover it: a directory, a
    /// file, or null when nothing is there.
    /// </summary>
    [SuppressVfsCompliance("OUT-OF-SCOPE: the kind of a physical path the VFS already resolved, read to build the kernel's view of the mounts for bubblewrap; the confined process, not the framework, uses the path.")]
    public static ConfinedAccess? HiddenKindOf(string physicalPath)
    {
        // OUT-OF-SCOPE: host probe of a VFS-resolved physical path (see the attribute).
        if (Directory.Exists(physicalPath))
            return ConfinedAccess.HiddenDirectory;
        if (File.Exists(physicalPath))
            return ConfinedAccess.HiddenFile;
        return null;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Availability probe: any failure starting or waiting for bwrap means confinement is unavailable, with the failure as the reason; it is never propagated.")]
    private static BubblewrapAvailability Probe()
    {
        if (!OperatingSystem.IsLinux())
            return BubblewrapAvailability.Unavailable("bubblewrap confines a process on Linux only");

        var executable = Locate();
        if (executable is null)
            return BubblewrapAvailability.Unavailable("bwrap was not found (install the bubblewrap package)");

        // The namespaces and mounts of a real call, around a command that only exits.
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in (string[])
                 ["--unshare-all", "--die-with-parent", "--new-session", "--ro-bind", "/", "/",
                  "--proc", "/proc", "--dev", "/dev", "--tmpfs", "/tmp", "--", "true"])
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return BubblewrapAvailability.Unavailable("bwrap could not be started");

            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(s_probeTimeout))
            {
                process.Kill(entireProcessTree: true);
                return BubblewrapAvailability.Unavailable("bwrap did not answer its probe within 10 seconds");
            }

            if (process.ExitCode == 0)
                return BubblewrapAvailability.Available(executable);

            var firstLine = stderr.GetAwaiter().GetResult()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return BubblewrapAvailability.Unavailable(
                $"bwrap cannot create its namespaces here ({firstLine ?? $"exit code {process.ExitCode}"})");
        }
        catch (Exception ex)
        {
            return BubblewrapAvailability.Unavailable($"bwrap could not be started ({ex.Message})");
        }
    }

    [SuppressVfsCompliance("OUT-OF-SCOPE: probes the host for the bwrap binary, as the docker sandbox probes for docker; not a VFS path.")]
    private static string? Locate()
    {
        // OUT-OF-SCOPE: binary discovery on the host, not a VFS mount.
        var known = s_knownPaths.FirstOrDefault(File.Exists);
        if (known is not null)
            return known;

        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(Path.IsPathRooted)
            .Select(directory => Path.Combine(directory, "bwrap"))
            .FirstOrDefault(File.Exists);
    }
}
