using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.Process;

/// <summary>Where a located <c>orkeon</c> binary was found.</summary>
public enum BinarySource
{
    /// <summary>Not found anywhere.</summary>
    NotFound,

    /// <summary>Next to Studio — the co-installed binary the packages guarantee.</summary>
    InstallDirectory,

    /// <summary>On <c>PATH</c> — a separately installed CLI.</summary>
    SearchPath,
}

/// <summary>
/// The outcome of a binary lookup: a path, or an actionable message plus the list of
/// everything that was tried, so the UI can show the user where Studio looked.
/// </summary>
public sealed record BinaryLocation
{
    /// <summary>Full path of the binary, null when it was not found.</summary>
    public string? Path { get; init; }

    /// <summary>Which lookup step matched.</summary>
    public BinarySource Source { get; init; } = BinarySource.NotFound;

    /// <summary>User-facing explanation, set only when the binary was not found.</summary>
    public string? Error { get; init; }

    /// <summary>Every candidate path that was probed, in order.</summary>
    public IReadOnlyList<string> ProbedPaths { get; init; } = [];

    /// <summary>True when <see cref="Path"/> holds a usable binary.</summary>
    public bool Found => Path is not null;
}

/// <summary>
/// Finds the co-installed <c>orkeon</c> executable.
/// <para>
/// The install directory comes first, and deliberately so: every onboarding channel ships
/// the CLI alongside Studio (zip/tarball <c>bin/</c>, the <c>.deb</c> payload under
/// <c>/usr/lib/orkeon</c> that the <c>/usr/bin</c> wrappers point at, the MSI install
/// folder). Preferring it means Studio drives the binary it was shipped with, not whichever
/// older <c>orkeon</c> happens to sit earlier on <c>PATH</c>. <c>PATH</c> is the fallback for
/// development trees and for a CLI installed on its own.
/// </para>
/// </summary>
[SuppressVfsCompliance(
    "OUT-OF-SCOPE: system binary discovery (the co-installed orkeon executable), the same " +
    "exception the CLI's esbuild resolution carries; no VFS mount exists for install directories.")]
public sealed class OrkeonBinaryLocator
{
    /// <summary>Name of the CLI executable, without extension.</summary>
    public const string ExecutableBaseName = "orkeon";

    private readonly IExecutableProbe _probe;
    private readonly IReadOnlyList<string> _fileNames;

    /// <summary>Creates a locator over <paramref name="probe"/>.</summary>
    /// <param name="probe">File-system and <c>PATH</c> access used for the lookup.</param>
    /// <param name="fileNames">
    /// Candidate file names, most specific first; defaults to the platform's
    /// (<c>orkeon.exe</c> then <c>orkeon</c> on Windows, <c>orkeon</c> elsewhere).
    /// </param>
    public OrkeonBinaryLocator(IExecutableProbe probe, IReadOnlyList<string>? fileNames = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _fileNames = fileNames is { Count: > 0 } ? fileNames : DefaultFileNames();
    }

    /// <summary>A locator over the real machine.</summary>
    public static OrkeonBinaryLocator ForCurrentMachine() => new(PhysicalExecutableProbe.Instance);

    /// <summary>The executable names to try on the current platform.</summary>
    public static IReadOnlyList<string> DefaultFileNames() =>
        OperatingSystem.IsWindows()
            ? [ExecutableBaseName + ".exe", ExecutableBaseName]
            : [ExecutableBaseName];

    /// <summary>
    /// Runs the lookup. Never throws: a missing binary is a <see cref="BinaryLocation"/>
    /// carrying the message to show, because "the CLI is not installed" is an ordinary state
    /// of a freshly opened Studio, not an exceptional one.
    /// </summary>
    public BinaryLocation Locate()
    {
        var probed = new List<string>();

        foreach (var directory in InstallDirectories())
        {
            if (TryDirectory(directory, probed, out var found))
                return new BinaryLocation { Path = found, Source = BinarySource.InstallDirectory, ProbedPaths = probed };
        }

        foreach (var directory in _probe.SearchPathDirectories)
        {
            if (TryDirectory(directory, probed, out var found))
                return new BinaryLocation { Path = found, Source = BinarySource.SearchPath, ProbedPaths = probed };
        }

        return new BinaryLocation
        {
            Source = BinarySource.NotFound,
            ProbedPaths = probed,
            Error =
                $"The `{ExecutableBaseName}` command-line tool was not found. Studio runs crews by " +
                $"invoking it, so nothing can be launched until it is installed. It normally sits next " +
                $"to Studio ({_probe.BaseDirectory}); reinstall the Orkeon package to restore it, or " +
                $"install the CLI separately and make sure its directory is on PATH.",
        };
    }

    /// <summary>
    /// The directories that may hold the co-installed binary, in order: next to Studio, then
    /// the <c>bin/</c> sibling and parent layouts the archives and the <c>.deb</c> produce.
    /// </summary>
    private IEnumerable<string> InstallDirectories()
    {
        var baseDirectory = _probe.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory))
            yield break;

        var trimmed = baseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in new[]
                 {
                     trimmed,
                     System.IO.Path.Combine(trimmed, "bin"),
                     System.IO.Path.GetDirectoryName(trimmed),
                     System.IO.Path.Combine(System.IO.Path.GetDirectoryName(trimmed) ?? trimmed, "bin"),
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
                yield return candidate;
        }
    }

    private bool TryDirectory(string directory, List<string> probed, out string? found)
    {
        foreach (var fileName in _fileNames)
        {
            var candidate = System.IO.Path.Combine(directory, fileName);
            probed.Add(candidate);
            if (_probe.FileExists(candidate))
            {
                found = candidate;
                return true;
            }
        }

        found = null;
        return false;
    }
}
