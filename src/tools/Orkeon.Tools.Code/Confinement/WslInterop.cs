using Orkeon.Compliance.Vfs;

namespace Orkeon.Tools.Code.Confinement;

/// <summary>
/// What the shell tool needs to know of WSL (#302): whether it runs there, how a <c>/mnt/&lt;d&gt;/…</c>
/// path is spelt for Windows, and whether a command is a Windows program (a <c>.exe</c> of a Windows
/// drive, run through the interop), which understands only the Windows spelling of a path.
/// </summary>
/// <param name="environment">Reads an environment variable; the process's by default.</param>
/// <param name="readFile">Reads a host file, null when absent; the host's file system by default.</param>
/// <param name="fileExists">Whether a host file exists; the host's file system by default.</param>
internal sealed class WslInterop(
    Func<string, string?> environment, Func<string, string?> readFile, Func<string, bool> fileExists)
{
    /// <summary>The variable WSL sets in every process of a distribution.</summary>
    public const string DistroVariable = "WSL_DISTRO_NAME";

    /// <summary>The kernel release, which names Microsoft under WSL.</summary>
    internal const string OsReleaseFile = "/proc/sys/kernel/osrelease";

    /// <summary>Where WSL mounts the Windows drives (its default automount root).</summary>
    internal const string DriveRoot = "/mnt/";

    /// <summary>The interop over the host's environment and file system; off WSL's Linux, one that sees no WSL.</summary>
    public static WslInterop Host { get; } = OperatingSystem.IsLinux()
        ? new(Environment.GetEnvironmentVariable, HostReadFile, HostFileExists)
        : new(_ => null, _ => null, _ => false);

    private bool? _isWsl;

    /// <summary>True when the process runs in a WSL distribution; probed once.</summary>
    public bool IsWsl => _isWsl ??= !string.IsNullOrEmpty(environment(DistroVariable))
        || (readFile(OsReleaseFile)?.Contains("microsoft", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// The Windows spelling of a path on a Windows drive: <c>/mnt/c/proj/a.ts</c> → <c>C:\proj\a.ts</c>,
    /// <c>/mnt/c</c> → <c>C:\</c>. Null for any other path: a path of the distribution has no drive.
    /// </summary>
    public static string? DriveSpelling(string posixPath)
    {
        ArgumentNullException.ThrowIfNull(posixPath);
        if (!posixPath.StartsWith(DriveRoot, StringComparison.Ordinal) || posixPath.Length < DriveRoot.Length + 1)
            return null;

        var letter = posixPath[DriveRoot.Length];
        var rest = posixPath[(DriveRoot.Length + 1)..];
        if (!char.IsAsciiLetter(letter) || (rest.Length > 0 && rest[0] != '/'))
            return null;

        var tail = rest.TrimEnd('/');
        return char.ToUpperInvariant(letter) + ":" + (tail.Length == 0 ? "\\" : tail.Replace('/', '\\'));
    }

    /// <summary>
    /// True when <paramref name="executable"/>, a bare name, resolves on the <c>PATH</c> to a <c>.exe</c>
    /// of a Windows drive (<c>cmd.exe</c>, <c>code.exe</c>, a Windows <c>dotnet.exe</c>).
    /// </summary>
    public bool IsWindowsProgram(string executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        if (!executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || executable.Contains('/', StringComparison.Ordinal))
            return false;

        foreach (var directory in (environment("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = directory.TrimEnd('/') + "/" + executable;
            if (fileExists(candidate))
                return DriveSpelling(candidate) is not null;
        }

        return false;
    }

    [SuppressVfsCompliance("OUT-OF-SCOPE: reads the kernel release to tell WSL, system probing; not a VFS path.")]
    private static string? HostReadFile(string path)
    {
        // OUT-OF-SCOPE: system probing of the kernel release, not a VFS mount.
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
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

    [SuppressVfsCompliance("OUT-OF-SCOPE: looks a command up on the PATH, as the bubblewrap probe looks for bwrap; not a VFS path.")]
    private static bool HostFileExists(string path)
    {
        // OUT-OF-SCOPE: system probing of an executable on the PATH, not a VFS mount.
        return File.Exists(path);
    }
}
