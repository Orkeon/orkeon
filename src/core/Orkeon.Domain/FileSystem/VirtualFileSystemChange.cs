namespace Orkeon.Domain.FileSystem;

/// <summary>Classifies the type of change observed on a virtual file system entry.</summary>
public enum VirtualFileChangeKind
{
    /// <summary>A new file or directory was created.</summary>
    Created,
    /// <summary>An existing file or directory was modified.</summary>
    Modified,
    /// <summary>A file or directory was deleted.</summary>
    Deleted,
    /// <summary>A file or directory was renamed or moved.</summary>
    Renamed
}

/// <summary>Describes a single change event observed on the virtual file system.</summary>
/// <param name="VirtualPath">Virtual path of the file or directory that changed.</param>
/// <param name="Kind">The kind of change that occurred.</param>
/// <param name="TimestampUtc">UTC timestamp at which the change was detected.</param>
/// <param name="OldVirtualPath">Previous virtual path before a rename; <c>null</c> for non-rename events.</param>
public sealed record VirtualFileSystemChange(
    string VirtualPath,
    VirtualFileChangeKind Kind,
    DateTimeOffset TimestampUtc,
    string? OldVirtualPath = null);
