using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.Process;

/// <summary>Where a located <c>orkeon</c> binary was found.</summary>
public enum BinarySource
{
    /// <summary>Not found anywhere.</summary>
    NotFound,

    /// <summary>In the directory the operator named (the <c>--cli-dir</c> argument).</summary>
    ExplicitDirectory,

    /// <summary>Next to Studio — the co-installed binary the packages guarantee.</summary>
    InstallDirectory,

    /// <summary>In the directory named by the <c>ORKEON_CLI_DIR</c> environment variable.</summary>
    EnvironmentVariable,

    /// <summary>On <c>PATH</c> — a separately installed CLI.</summary>
    SearchPath,

    /// <summary>
    /// In the repository checkout Studio itself runs from — the F5-from-the-IDE layout,
    /// where the CLI builds into <c>src/scripting/Orkeon.Scripting.Cli/bin/…</c> instead
    /// of sitting next to Studio.
    /// </summary>
    DevelopmentTree,
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

    /// <summary>Environment variable naming the directory that holds the CLI.</summary>
    public const string DirectoryEnvironmentVariable = "ORKEON_CLI_DIR";

    /// <summary>
    /// Process-wide directory override, set once at startup from the front-end's
    /// <c>--cli-dir</c> argument. Ambient on purpose: the three CLI clients
    /// (runner, forge, run) all build their default locator through
    /// <see cref="ForCurrentMachine"/>, and the operator's choice must reach every one
    /// of them without re-plumbing each seam.
    /// </summary>
    public static string? DirectoryOverride { get; set; }

    private readonly IExecutableProbe _probe;
    private readonly IReadOnlyList<string> _fileNames;
    private readonly string? _explicitDirectory;
    private readonly Func<string, string?> _environment;

    /// <summary>Creates a locator over <paramref name="probe"/>.</summary>
    /// <param name="probe">File-system and <c>PATH</c> access used for the lookup.</param>
    /// <param name="fileNames">
    /// Candidate file names, most specific first; defaults to the platform's
    /// (<c>orkeon.exe</c> then <c>orkeon</c> on Windows, <c>orkeon</c> elsewhere).
    /// </param>
    public OrkeonBinaryLocator(
        IExecutableProbe probe,
        IReadOnlyList<string>? fileNames = null,
        string? explicitDirectory = null,
        Func<string, string?>? environment = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _fileNames = fileNames is { Count: > 0 } ? fileNames : DefaultFileNames();
        _explicitDirectory = explicitDirectory;
        _environment = environment ?? Environment.GetEnvironmentVariable;
    }

    /// <summary>A locator over the real machine, honouring <see cref="DirectoryOverride"/>.</summary>
    public static OrkeonBinaryLocator ForCurrentMachine() =>
        new(PhysicalExecutableProbe.Instance, explicitDirectory: DirectoryOverride);

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

        // 1. The directory the operator named beats everything: an explicit choice must
        //    never lose to whatever happens to sit next to Studio.
        if (_explicitDirectory is { Length: > 0 } explicitDirectory
            && TryDirectory(explicitDirectory, probed, out var explicitFound))
        {
            return new BinaryLocation { Path = explicitFound, Source = BinarySource.ExplicitDirectory, ProbedPaths = probed };
        }

        // 2. Next to the executable — the layout every package guarantees.
        foreach (var directory in InstallDirectories())
        {
            if (TryDirectory(directory, probed, out var found))
                return new BinaryLocation { Path = found, Source = BinarySource.InstallDirectory, ProbedPaths = probed };
        }

        // 3. The environment variable — machine-wide configuration without a flag.
        if (_environment(DirectoryEnvironmentVariable) is { Length: > 0 } environmentDirectory
            && TryDirectory(environmentDirectory.Trim(), probed, out var environmentFound))
        {
            return new BinaryLocation { Path = environmentFound, Source = BinarySource.EnvironmentVariable, ProbedPaths = probed };
        }

        // 4. PATH, then 5. the development checkout.
        foreach (var directory in _probe.SearchPathDirectories)
        {
            if (TryDirectory(directory, probed, out var found))
                return new BinaryLocation { Path = found, Source = BinarySource.SearchPath, ProbedPaths = probed };
        }

        foreach (var directory in DevelopmentTreeDirectories())
        {
            if (TryDirectory(directory, probed, out var found))
                return new BinaryLocation { Path = found, Source = BinarySource.DevelopmentTree, ProbedPaths = probed };
        }

        return new BinaryLocation
        {
            Source = BinarySource.NotFound,
            ProbedPaths = probed,
            Error =
                $"The `{ExecutableBaseName}` command-line tool was not found. Studio runs crews by " +
                $"invoking it, so nothing can be launched until it is installed. Looked, in order: " +
                $"the --cli-dir argument, next to Studio ({_probe.BaseDirectory}), the " +
                $"{DirectoryEnvironmentVariable} environment variable, PATH, and the development " +
                $"checkout. Reinstall the Orkeon package, point --cli-dir or " +
                $"{DirectoryEnvironmentVariable} at the CLI's directory, or in a checkout build it " +
                $"first: dotnet build src/scripting/Orkeon.Scripting.Cli.",
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

    /// <summary>
    /// The dev-checkout fallback: when Studio runs from its own <c>bin/</c> inside a clone
    /// of the repository (detected by walking up to <c>Orkeon.sln</c>), the CLI — if built —
    /// sits in <c>src/scripting/Orkeon.Scripting.Cli/bin/&lt;Configuration&gt;/&lt;tfm&gt;/</c>.
    /// Studio's own base path names the configuration (the segment after <c>bin</c>) and the
    /// TFM (its own, minus the <c>-windows</c> suffix); the sibling configuration is probed
    /// second so a Debug Studio can still find a Release CLI and vice versa.
    /// </summary>
    private IEnumerable<string> DevelopmentTreeDirectories()
    {
        var baseDirectory = _probe.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory))
            yield break;

        var segments = baseDirectory
            .Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            .Where(s => s.Length > 0)
            .ToArray();

        var binIndex = Array.FindLastIndex(segments, s => string.Equals(s, "bin", StringComparison.OrdinalIgnoreCase));
        var configuration = binIndex >= 0 && binIndex + 1 < segments.Length ? segments[binIndex + 1] : "Debug";
        var framework = segments.Length > 0 ? segments[^1] : "";
        var suffix = framework.IndexOf('-', StringComparison.Ordinal);
        if (suffix > 0)
            framework = framework[..suffix];
        if (framework.Length == 0 || !framework.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            yield break;

        var root = FindRepositoryRoot(baseDirectory);
        if (root is null)
            yield break;

        var cliBin = System.IO.Path.Combine(root, "src", "scripting", "Orkeon.Scripting.Cli", "bin");
        var configurations = string.Equals(configuration, "Release", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Release", "Debug" }
            : new[] { configuration, "Release" };

        foreach (var candidate in configurations.Distinct(StringComparer.OrdinalIgnoreCase))
            yield return System.IO.Path.Combine(cliBin, candidate, framework);
    }

    private string? FindRepositoryRoot(string startDirectory)
    {
        var directory = startDirectory.TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

        for (var depth = 0; depth < 10 && !string.IsNullOrEmpty(directory); depth++)
        {
            if (_probe.FileExists(System.IO.Path.Combine(directory, "Orkeon.sln")))
                return directory;

            directory = System.IO.Path.GetDirectoryName(directory);
        }

        return null;
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
