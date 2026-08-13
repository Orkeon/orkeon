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
    /// <summary>Shared stateless instance.</summary>
    public static PhysicalExecutableProbe Instance { get; } = new();

    /// <inheritdoc />
    public string BaseDirectory => AppContext.BaseDirectory;

    /// <inheritdoc />
    public IReadOnlyList<string> SearchPathDirectories
    {
        get
        {
            var path = System.Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
                return [];

            return path
                .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }
    }

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);
}
