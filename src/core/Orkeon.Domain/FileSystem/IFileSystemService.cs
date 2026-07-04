using Orkeon.Domain.Tools.Security;

namespace Orkeon.Domain.FileSystem;

/// <summary>
/// Service for resolving virtual file system paths, validating access rights,
/// and performing enumeration / streaming over mounted directories.
/// </summary>
public interface IFileSystemService
{
    /// <summary>
    /// Resolves a virtual path to a physical path and validates access rights.
    /// Returns <see cref="PathValidationResult.Denied(string)"/> if rights are insufficient
    /// or the path validator rejects the physical path.
    /// </summary>
    PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight);

    /// <summary>
    /// Converts a physical path back to its virtual path representation.
    /// Returns <c>null</c> if the physical path does not belong to any mount.
    /// </summary>
    string? ToVirtualPath(string physicalPath);

    /// <summary>
    /// Returns the list of available mounts with their virtual paths and rights.
    /// Physical base paths are never exposed.
    /// </summary>
    IReadOnlyList<MountInfo> GetAvailableMounts();

    /// <summary>
    /// Enumerates files and directories under <paramref name="virtualRoot"/> lazily.
    /// Patterns in <see cref="VirtualEnumerationOptions.Exclude"/> are applied to the relative
    /// path (relative to <paramref name="virtualRoot"/>, using <c>/</c> as separator).
    /// </summary>
    /// <exception cref="FileAccessDeniedException">Thrown when the root is outside any mount or lacks Read rights.</exception>
    IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
        string virtualRoot,
        VirtualEnumerationOptions? options,
        CancellationToken ct);

    /// <summary>
    /// Opens a read-only stream on the file at <paramref name="virtualPath"/>.
    /// The caller owns the stream and must dispose it.
    /// </summary>
    /// <exception cref="FileAccessDeniedException">Thrown when the path is outside any mount or lacks Read rights.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the virtual path points to no file.</exception>
    Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct);

    /// <summary>
    /// Reads all bytes from the file at <paramref name="virtualPath"/>.
    /// Returns <c>null</c> if the file does not exist.
    /// </summary>
    /// <exception cref="FileAccessDeniedException">Thrown when the path is outside any mount or lacks Read rights.</exception>
    Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct);

    /// <summary>
    /// Reads all text from the file at <paramref name="virtualPath"/> using UTF-8.
    /// Returns <c>null</c> if the file does not exist.
    /// </summary>
    /// <exception cref="FileAccessDeniedException">Thrown when the path is outside any mount or lacks Read rights.</exception>
    Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct);

    /// <summary>
    /// Returns the kind of the entry at <paramref name="virtualPath"/>
    /// (<see cref="VirtualEntryKind.File"/>, <see cref="VirtualEntryKind.Directory"/>, or <see cref="VirtualEntryKind.SymLink"/>).
    /// </summary>
    /// <exception cref="FileAccessDeniedException">Thrown when the path is outside any mount or lacks Read rights.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the virtual path points to nothing.</exception>
    Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct);

    /// <summary>
    /// Writes <paramref name="content"/> to the file at <paramref name="virtualPath"/>
    /// using UTF-8 <em>without</em> a byte-order mark, creating missing parent directories
    /// under the mount root. Overwrites any pre-existing file.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    /// <exception cref="FileAccessDeniedException">Thrown when the path is outside any mount or lacks Write/Create rights.</exception>
    Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct);

    /// <summary>
    /// Returns true if the virtual path exists (file, directory, or symlink).
    /// Never throws: access denied or missing path both return false.
    /// </summary>
    System.Threading.Tasks.Task<bool> ExistsAsync(string virtualPath, CancellationToken ct);

    /// <summary>
    /// Creates the directory at <paramref name="virtualPath"/> and any missing parent directories.
    /// Idempotent — succeeds if the directory already exists.
    /// </summary>
    /// <exception cref="FileAccessDeniedException">Thrown when the path is outside any mount or lacks Create rights.</exception>
    System.Threading.Tasks.Task CreateDirectoryAsync(string virtualPath, CancellationToken ct);

    /// <summary>
    /// Deletes the file or directory at <paramref name="virtualPath"/>.
    /// When <paramref name="recursive"/> is true, removes directory contents recursively.
    /// Returns false if the entry did not exist.
    /// </summary>
    /// <exception cref="FileAccessDeniedException">Thrown when the path is outside any mount or lacks Delete rights,
    /// or when recursive deletion would follow a symlink outside the mounts.</exception>
    System.Threading.Tasks.Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct);

    /// <summary>
    /// Writes raw bytes to the file at <paramref name="virtualPath"/>, creating missing
    /// parent directories and overwriting any pre-existing file.
    /// Requires <see cref="FileAccessRights.Write"/> | <see cref="FileAccessRights.Create"/>.
    /// </summary>
    /// <returns>The number of bytes written (equals <c>content.Length</c>).</returns>
    /// <exception cref="FileAccessDeniedException">Thrown when the path is outside any mount or lacks the required rights.</exception>
    Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct);

    /// <summary>
    /// Appends <paramref name="content"/> (UTF-8, no BOM) to the file at <paramref name="virtualPath"/>,
    /// creating the file and any missing parent directories if they do not exist.
    /// Requires <see cref="FileAccessRights.Write"/> | <see cref="FileAccessRights.Create"/>.
    /// </summary>
    /// <returns>The number of bytes appended (byte count of the UTF-8 encoded content).</returns>
    /// <exception cref="FileAccessDeniedException">Thrown when the path is outside any mount or lacks the required rights.</exception>
    Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct);

    /// <summary>
    /// Returns the metadata of the entry at <paramref name="virtualPath"/> without loading its content.
    /// Returns <c>null</c> if the entry does not exist or access is denied.
    /// Never throws <see cref="FileNotFoundException"/>.
    /// </summary>
    Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct);

    /// <summary>
    /// Opens a write stream on the file at <paramref name="virtualPath"/> (FileMode.Create, FileAccess.Write).
    /// Creates missing parent directories. Caller owns the stream and must dispose it.
    /// </summary>
    /// <exception cref="FileAccessDeniedException">Thrown when the path is outside any mount or lacks Write/Create rights.</exception>
    Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default);

    /// <summary>
    /// Opens an append stream on the file at <paramref name="virtualPath"/> (FileMode.Append, FileAccess.Write).
    /// Creates the file and missing parent directories if they do not exist. Caller owns the stream and must dispose it.
    /// </summary>
    /// <exception cref="FileAccessDeniedException">Thrown when the path is outside any mount or lacks Write/Create rights.</exception>
    Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default);

    /// <summary>
    /// Copies the file at <paramref name="srcVirtualPath"/> to <paramref name="dstVirtualPath"/>.
    /// Intra-mount copies use <see cref="File.Copy(string, string, bool)"/> for performance; cross-mount copies use a 64 KiB streaming buffer.
    /// </summary>
    /// <exception cref="FileAccessDeniedException">Thrown when src lacks Read rights or dst lacks Write/Create rights.</exception>
    /// <exception cref="IOException">Thrown when the destination file already exists and <paramref name="overwrite"/> is false.</exception>
    System.Threading.Tasks.Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default);
}
