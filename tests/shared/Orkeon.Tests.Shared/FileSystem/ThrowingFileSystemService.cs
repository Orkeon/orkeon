using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Tests.Shared.FileSystem;

/// <summary>
/// Strict-mode <see cref="IFileSystemService"/> stub: every interface member throws when
/// invoked. Replaces <c>Mock&lt;IFileSystemService&gt;(MockBehavior.Strict)</c>. Tests that
/// rely on this stub assert that the SUT short-circuited before reaching the file system.
/// </summary>
public sealed class ThrowingFileSystemService : IFileSystemService
{
    /// <summary>Total count of attempted interactions (incremented before throwing).</summary>
    public int CallCount { get; private set; }

    private T Fail<T>(string member)
    {
        CallCount++;
        throw new InvalidOperationException(
            $"ThrowingFileSystemService: unexpected call to {member}.");
    }

    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight) => Fail<PathValidationResult>(nameof(ResolveAndValidate));
    public string? ToVirtualPath(string physicalPath) => Fail<string?>(nameof(ToVirtualPath));
    public IReadOnlyList<MountInfo> GetAvailableMounts() => Fail<IReadOnlyList<MountInfo>>(nameof(GetAvailableMounts));
    public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct) => Fail<IAsyncEnumerable<VirtualFileEntry>>(nameof(EnumerateFilesAsync));
    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct) => Fail<Task<Stream>>(nameof(OpenReadStreamAsync));
    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct) => Fail<Task<byte[]?>>(nameof(TryReadAllBytesAsync));
    public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct) => Fail<Task<string?>>(nameof(TryReadAllTextAsync));
    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct) => Fail<Task<VirtualEntryKind>>(nameof(GetEntryKindAsync));
    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct) => Fail<Task<int>>(nameof(WriteAllTextAsync));
    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct) => Fail<Task<bool>>(nameof(ExistsAsync));
    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct) => Fail<Task>(nameof(CreateDirectoryAsync));
    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct) => Fail<Task<bool>>(nameof(DeleteAsync));
    public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct) => Fail<Task<int>>(nameof(WriteAllBytesAsync));
    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct) => Fail<Task<int>>(nameof(AppendAllTextAsync));
    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct) => Fail<Task<VirtualFileEntry?>>(nameof(TryGetEntryAsync));
    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default) => Fail<Task<Stream>>(nameof(OpenWriteStreamAsync));
    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default) => Fail<Task<Stream>>(nameof(OpenAppendStreamAsync));
    public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default) => Fail<Task>(nameof(CopyAsync));
}
