using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.Process;

/// <summary>
/// The machine facts <see cref="OrkeonBinaryLocator"/> needs: where Studio itself is
/// installed, what <c>PATH</c> holds, and whether a candidate file exists.
/// An interface so binary resolution is testable on a machine that has no <c>orkeon</c>
/// installed — and on one that has.
/// </summary>
public interface IExecutableProbe
{
    /// <summary>Directory holding the running Studio executable.</summary>
    string BaseDirectory { get; }

    /// <summary>Directories listed in <c>PATH</c>, in lookup order.</summary>
    IReadOnlyList<string> SearchPathDirectories { get; }

    /// <summary>True when <paramref name="path"/> names an existing file.</summary>
    bool FileExists(string path);
}

/// <summary>Real-machine <see cref="IExecutableProbe"/>.</summary>
[SuppressVfsCompliance(
    "OUT-OF-SCOPE: probes the co-installed orkeon binary on the physical disk and on PATH — " +
    "system binary discovery, outside any VFS mount, like the CLI's esbuild resolution.")]
public sealed class PhysicalExecutableProbe : IExecutableProbe
{
    /// <summary>
    /// The shared instance, and the only one anything builds: PATH is now read once behind
    /// it, so a second probe would not buy a fresher reading -- it would only re-read the
    /// same process copy, which no code outside this process can change.
    /// </summary>
    public static PhysicalExecutableProbe Instance { get; } = new();

    private readonly Lazy<IReadOnlyList<string>> _searchPathDirectories = new(ReadSearchPath);

    /// <inheritdoc />
    public string BaseDirectory => AppContext.BaseDirectory;

    /// <inheritdoc />
    /// <remarks>
    /// Split once, then handed out as is: a property must not rebuild a list on every read,
    /// and PATH is fixed for the lifetime of a process -- nothing in Studio writes it, and a
    /// change made outside reaches the next process, never this one.
    /// </remarks>
    public IReadOnlyList<string> SearchPathDirectories => _searchPathDirectories.Value;

    private static string[] ReadSearchPath()
    {
        var path = System.Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return [];

        return path.Split(
            System.IO.Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);
}
