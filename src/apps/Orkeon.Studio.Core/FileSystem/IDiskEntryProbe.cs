using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.FileSystem;

/// <summary>What a physical path names on the disk.</summary>
public enum DiskEntryKind
{
    /// <summary>Nothing — the path does not exist, or cannot be reached.</summary>
    None,

    /// <summary>A file.</summary>
    File,

    /// <summary>A directory.</summary>
    Directory,
}

/// <summary>
/// Tells a file from a directory, and both from nothing (STUDIO-47): what was dropped on the
/// wizard's need is inserted only when it exists, and a directory becomes a folder of the
/// team where a file gives its parent. An interface so the wizard is tested without a disk.
/// </summary>
public interface IDiskEntryProbe
{
    /// <summary>What <paramref name="path"/> names on the disk.</summary>
    DiskEntryKind KindOf(string path);
}

/// <summary>Real-disk <see cref="IDiskEntryProbe"/>.</summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; a path dropped from the OS explorer is " +
    "probed before it becomes a VFS mount root, before any VFS exists.")]
public sealed class PhysicalDiskEntryProbe : IDiskEntryProbe
{
    /// <summary>Shared stateless instance.</summary>
    public static PhysicalDiskEntryProbe Instance { get; } = new();

    /// <inheritdoc />
    public DiskEntryKind KindOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return DiskEntryKind.None;

        if (Directory.Exists(path))
            return DiskEntryKind.Directory;

        return File.Exists(path) ? DiskEntryKind.File : DiskEntryKind.None;
    }
}
