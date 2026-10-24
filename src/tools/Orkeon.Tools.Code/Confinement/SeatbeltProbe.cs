using System.Diagnostics;
using Orkeon.Compliance.Vfs;
using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Confinement;

/// <summary>
/// Checks once per process that Seatbelt can confine a process on this host: the system is macOS,
/// <c>/usr/bin/sandbox-exec</c> exists and runs a command under a profile. A host where it cannot (a
/// system without it, a process already sandboxed in a way that forbids nesting) makes a confined call
/// fail rather than run unconfined. It also resolves the links of a path, since Seatbelt matches the
/// path a link leads to.
/// </summary>
internal static class SeatbeltProbe
{
    /// <summary>
    /// The program that applies a profile. An absolute path: a binary planted ahead of the system's on
    /// <c>PATH</c> is never the one that confines (S4036).
    /// </summary>
    public const string SandboxExec = "/usr/bin/sandbox-exec";

    private const int MaxLinks = 40;

    private static readonly TimeSpan s_probeTimeout = TimeSpan.FromSeconds(10);

    private static readonly Lazy<ShellSandboxAvailability> s_current = new(Probe);

    /// <summary>The availability probed at the first call of the process, then remembered.</summary>
    public static ShellSandboxAvailability Current() => s_current.Value;

    /// <summary>
    /// <paramref name="path"/> with every link along it resolved, <c>.</c> and <c>..</c> removed: the
    /// path Seatbelt matches (<c>/var/folders/...</c> is <c>/private/var/folders/...</c>). The part
    /// that does not exist yet is kept as written.
    /// </summary>
    /// <param name="path">An absolute path.</param>
    /// <param name="linkTarget">Reads the target of a link, null when the path is not one.</param>
    /// <exception cref="ArgumentException">The path is not absolute, or loops through its links.</exception>
    public static string Canonicalize(string path, Func<string, string?> linkTarget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(linkTarget);
        if (path[0] != '/')
            throw new ArgumentException($"A confined path must be absolute: '{path}'.", nameof(path));

        var pending = new Stack<string>(path.Split('/', StringSplitOptions.RemoveEmptyEntries).Reverse());
        var resolved = new List<string>();
        var links = 0;
        while (pending.TryPop(out var part))
        {
            if (StepsInPlace(part, resolved))
                continue;

            var candidate = "/" + string.Join('/', resolved.Append(part));
            var target = linkTarget(candidate);
            if (string.IsNullOrEmpty(target))
            {
                resolved.Add(part);
                continue;
            }

            if (++links > MaxLinks)
                throw new ArgumentException($"Too many links along '{path}'.", nameof(path));

            // An absolute target restarts from the root, a relative one from the link's directory.
            if (target[0] == '/')
                resolved.Clear();
            foreach (var segment in target.Split('/', StringSplitOptions.RemoveEmptyEntries).Reverse())
                pending.Push(segment);
        }

        return "/" + string.Join('/', resolved);
    }

    /// <summary>
    /// Applies <c>.</c> (nothing) and <c>..</c> (one level up) to <paramref name="resolved"/>; false
    /// for any other part, which the caller resolves.
    /// </summary>
    private static bool StepsInPlace(string part, List<string> resolved)
    {
        if (part == ".")
            return true;
        if (part != "..")
            return false;
        if (resolved.Count > 0)
            resolved.RemoveAt(resolved.Count - 1);
        return true;
    }

    /// <summary><paramref name="path"/> canonicalized against the host's file system.</summary>
    public static string Canonicalize(string path) => Canonicalize(path, ReadLink);

    [SuppressVfsCompliance("OUT-OF-SCOPE: reads the target of a link along a physical path the VFS already resolved, so the Seatbelt profile names the path the kernel matches; the confined process, not the framework, uses the path.")]
    private static string? ReadLink(string path)
    {
        // OUT-OF-SCOPE: host probe of a VFS-resolved physical path (see the attribute).
        try
        {
            return new FileInfo(path).LinkTarget;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Availability probe: any failure starting or waiting for sandbox-exec means confinement is unavailable, with the failure as the reason; it is never propagated.")]
    private static ShellSandboxAvailability Probe()
    {
        if (!OperatingSystem.IsMacOS())
            return new ShellSandboxAvailability("Seatbelt confines a process on macOS only");
        if (!SandboxExecExists())
            return new ShellSandboxAvailability($"{SandboxExec} was not found");

        // A profile that allows everything, around a command that only exits: the probe checks that
        // a profile can be applied, not the profile of a call.
        var startInfo = new ProcessStartInfo(SandboxExec)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in (string[])["-p", "(version 1)(allow default)", "/usr/bin/true"])
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return new ShellSandboxAvailability("sandbox-exec could not be started");

            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(s_probeTimeout))
            {
                process.Kill(entireProcessTree: true);
                return new ShellSandboxAvailability("sandbox-exec did not answer its probe within 10 seconds");
            }

            if (process.ExitCode == 0)
                return ShellSandboxAvailability.Available;

            var firstLine = stderr.GetAwaiter().GetResult()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return new ShellSandboxAvailability(
                $"sandbox-exec cannot apply a profile here ({firstLine ?? $"exit code {process.ExitCode}"})");
        }
        catch (Exception ex)
        {
            return new ShellSandboxAvailability($"sandbox-exec could not be started ({ex.Message})");
        }
    }

    [SuppressVfsCompliance("OUT-OF-SCOPE: probes the host for the sandbox-exec binary, as the docker sandbox probes for docker; not a VFS path.")]
    private static bool SandboxExecExists()
        // OUT-OF-SCOPE: binary discovery on the host, not a VFS mount.
        => File.Exists(SandboxExec);
}
