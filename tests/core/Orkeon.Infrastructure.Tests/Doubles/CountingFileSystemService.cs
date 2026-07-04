using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Delegating decorator over <see cref="IFileSystemService"/> that counts byte reads.
/// Used to prove that the A2A mTLS client certificate is read exactly once across calls
/// (R9.2 / ANT-018 — on e91ef936 every A2A call re-read and re-imported the PFX).
/// </summary>
public sealed class CountingFileSystemService : IFileSystemService
{
    private readonly IFileSystemService _inner;
    private int _readAllBytesCalls;

    public CountingFileSystemService(IFileSystemService inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Number of <see cref="TryReadAllBytesAsync"/> invocations.</summary>
    public int ReadAllBytesCalls => Volatile.Read(ref _readAllBytesCalls);

    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
    {
        Interlocked.Increment(ref _readAllBytesCalls);
        return _inner.TryReadAllBytesAsync(virtualPath, ct);
    }

    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
        => _inner.ResolveAndValidate(virtualPath, requiredRight);

    public string? ToVirtualPath(string physicalPath) => _inner.ToVirtualPath(physicalPath);

    public IReadOnlyList<MountInfo> GetAvailableMounts() => _inner.GetAvailableMounts();

    public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
        string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct)
        => _inner.EnumerateFilesAsync(virtualRoot, options, ct);

    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct)
        => _inner.OpenReadStreamAsync(virtualPath, ct);

    public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
        => _inner.TryReadAllTextAsync(virtualPath, ct);

    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct)
        => _inner.GetEntryKindAsync(virtualPath, ct);

    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
        => _inner.WriteAllTextAsync(virtualPath, content, ct);

    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct)
        => _inner.ExistsAsync(virtualPath, ct);

    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct)
        => _inner.CreateDirectoryAsync(virtualPath, ct);

    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct)
        => _inner.DeleteAsync(virtualPath, recursive, ct);

    public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct)
        => _inner.WriteAllBytesAsync(virtualPath, content, ct);

    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct)
        => _inner.AppendAllTextAsync(virtualPath, content, ct);

    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
        => _inner.TryGetEntryAsync(virtualPath, ct);

    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default)
        => _inner.OpenWriteStreamAsync(virtualPath, ct);

    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default)
        => _inner.OpenAppendStreamAsync(virtualPath, ct);

    public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default)
        => _inner.CopyAsync(srcVirtualPath, dstVirtualPath, overwrite, ct);
}
