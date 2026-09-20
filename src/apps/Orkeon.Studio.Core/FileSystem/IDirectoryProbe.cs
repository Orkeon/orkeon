using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.FileSystem;

/// <summary>
/// Disk access needed by the mount editor: checking that a picked physical path
/// exists, and creating it when the user accepts the "create the folder" remediation.
/// It is an interface so validation can be exercised without touching the real disk.
/// </summary>
public interface IDirectoryProbe
{
    /// <summary>True when the directory exists.</summary>
    bool Exists(string path);

    /// <summary>Creates the directory (and its parents); a no-op when it already exists.</summary>
    void Create(string path);
}

/// <summary>Real-disk <see cref="IDirectoryProbe"/>.</summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; the mount editor probes physical " +
    "paths that are about to become VFS mount roots, before any VFS exists.")]
public sealed class PhysicalDirectoryProbe : IDirectoryProbe
{
    /// <summary>Shared stateless instance.</summary>
    public static PhysicalDirectoryProbe Instance { get; } = new();

    /// <inheritdoc />
    public bool Exists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public void Create(string path) => Directory.CreateDirectory(path);
}
