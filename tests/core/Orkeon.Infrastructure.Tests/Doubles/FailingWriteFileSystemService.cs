using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Delegating <see cref="IFileSystemService"/> whose next <see cref="FailNextJsonWrites"/> writes to a
/// <c>.json</c> file truncate the target (a torn write) and then throw an <see cref="IOException"/>.
/// </summary>
public sealed class FailingWriteFileSystemService : IFileSystemService
{
    private readonly IFileSystemService _inner;
    private int _failures;

    public FailingWriteFileSystemService(IFileSystemService inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Number of upcoming writes to a <c>.json</c> path that tear and fail.</summary>
    public int FailNextJsonWrites
    {
        get => Volatile.Read(ref _failures);
        set => Volatile.Write(ref _failures, value);
    }

    private async Task TearAsync(string virtualPath, CancellationToken ct)
    {
        if (!virtualPath.EndsWith(".json", StringComparison.Ordinal)) return;
        if (Interlocked.Decrement(ref _failures) < 0)
        {
            Interlocked.Increment(ref _failures);
            return;
        }
        await _inner.WriteAllTextAsync(virtualPath, string.Empty, ct);
        throw new IOException("simulated write failure");
    }

    public async Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
    {
        await TearAsync(virtualPath, ct);
        return await _inner.WriteAllTextAsync(virtualPath, content, ct);
    }

    public async Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct)
    {
        await TearAsync(virtualPath, ct);
        return await _inner.WriteAllBytesAsync(virtualPath, content, ct);
    }

    public async Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct)
    {
        await TearAsync(virtualPath, ct);
        return await _inner.AppendAllTextAsync(virtualPath, content, ct);
    }

    public async Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default)
    {
        await TearAsync(virtualPath, ct);
        return await _inner.OpenWriteStreamAsync(virtualPath, ct);
    }

    public async Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default)
    {
        await TearAsync(virtualPath, ct);
        return await _inner.OpenAppendStreamAsync(virtualPath, ct);
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

    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
        => _inner.TryReadAllBytesAsync(virtualPath, ct);

    public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
        => _inner.TryReadAllTextAsync(virtualPath, ct);

    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct)
        => _inner.GetEntryKindAsync(virtualPath, ct);

    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct)
        => _inner.ExistsAsync(virtualPath, ct);

    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct)
        => _inner.CreateDirectoryAsync(virtualPath, ct);

    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct)
        => _inner.DeleteAsync(virtualPath, recursive, ct);

    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
        => _inner.TryGetEntryAsync(virtualPath, ct);

    public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default)
        => _inner.CopyAsync(srcVirtualPath, dstVirtualPath, overwrite, ct);
}
