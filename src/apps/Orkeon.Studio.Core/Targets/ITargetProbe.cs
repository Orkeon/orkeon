using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.Targets;

/// <summary>
/// Disk access needed to recognise a run target: is the picked path a file or a
/// directory, does the directory hold the marker sub-folders of a multi-file crew, and
/// which <c>*.ork.ts</c> scripts does it contain. An interface so detection can be
/// exercised on declared trees rather than on the machine's disk.
/// </summary>
public interface ITargetProbe
{
    /// <summary>True when the path is an existing directory.</summary>
    bool DirectoryExists(string path);

    /// <summary>True when the path is an existing file.</summary>
    bool FileExists(string path);

    /// <summary>
    /// Files directly under <paramref name="directory"/> matching <paramref name="searchPattern"/>
    /// (no recursion), as full paths sorted ordinally so detection is deterministic.
    /// Returns an empty list when the directory does not exist.
    /// </summary>
    IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern);
}

/// <summary>Real-disk <see cref="ITargetProbe"/>.</summary>
[SuppressVfsCompliance(
    "OUT-OF-SCOPE: Studio inspects a path the user picked in a file browser, outside any " +
    "mount, to decide which crew definition the co-installed `orkeon` CLI should be pointed at.")]
public sealed class PhysicalTargetProbe : ITargetProbe
{
    /// <summary>Shared stateless instance.</summary>
    public static PhysicalTargetProbe Instance { get; } = new();

    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern)
    {
        if (!Directory.Exists(directory))
            return [];

        var files = Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);
        return files;
    }
}
