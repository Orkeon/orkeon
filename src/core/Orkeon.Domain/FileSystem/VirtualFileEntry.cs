namespace Orkeon.Domain.FileSystem;

/// <summary>
/// Entry returned by <see cref="IFileSystemService.EnumerateFilesAsync"/> and
/// <see cref="IFileSystemService.TryGetEntryAsync"/>.
/// Contains only virtual path metadata — physical paths are never exposed to callers.
/// </summary>
/// <param name="VirtualPath">Virtual path of the entry (starts with a mount virtual prefix, e.g. <c>/src/app/x.ts</c>).</param>
/// <param name="SizeBytes">Size of the file in bytes. Zero for directories.</param>
/// <param name="LastModified">Last modification timestamp (UTC).</param>
/// <param name="Kind">Entry kind: file, directory, or symbolic link.</param>
/// <param name="CreationTime">Creation timestamp (UTC). Null if unavailable.</param>
/// <param name="LinkTarget">
/// Virtual path of the symlink target when <paramref name="Kind"/> is <see cref="VirtualEntryKind.SymLink"/>;
/// <c>null</c> otherwise. If the target is outside all mounts, the value is prefixed with <c>ext::</c>.
/// </param>
public sealed record VirtualFileEntry(
    string VirtualPath,
    long SizeBytes,
    DateTimeOffset LastModified,
    VirtualEntryKind Kind,
    DateTimeOffset? CreationTime = null,
    string? LinkTarget = null);
