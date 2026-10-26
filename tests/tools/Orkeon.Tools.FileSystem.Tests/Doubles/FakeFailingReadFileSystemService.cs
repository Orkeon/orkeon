using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Tools.FileSystem.Tests.Doubles;

/// <summary>Delegating decorator whose reads of the listed virtual paths fail with an <see cref="IOException"/>.</summary>
public sealed class FakeFailingReadFileSystemService(IFileSystemService inner, params string[] failingPaths) : IFileSystemService
{
    private void ThrowIfFailing(string p)
    {
        if (failingPaths.Contains(p, StringComparer.Ordinal)) throw new IOException("simulated read failure");
    }

    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
    {
        ThrowIfFailing(virtualPath);
        return inner.TryReadAllBytesAsync(virtualPath, ct);
    }

    public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
    {
        ThrowIfFailing(virtualPath);
        return inner.TryReadAllTextAsync(virtualPath, ct);
    }

    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct)
    {
        ThrowIfFailing(virtualPath);
        return inner.OpenReadStreamAsync(virtualPath, ct);
    }

    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
        => inner.ResolveAndValidate(virtualPath, requiredRight);

    public string? ToVirtualPath(string physicalPath) => inner.ToVirtualPath(physicalPath);

    public string? WorkingDirectory => inner.WorkingDirectory;

    public IReadOnlyList<MountInfo> GetAvailableMounts() => inner.GetAvailableMounts();

    public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
        string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct)
        => inner.EnumerateFilesAsync(virtualRoot, options, ct);

    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct)
        => inner.GetEntryKindAsync(virtualPath, ct);

    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
        => inner.WriteAllTextAsync(virtualPath, content, ct);

    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct)
        => inner.ExistsAsync(virtualPath, ct);

    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct)
        => inner.CreateDirectoryAsync(virtualPath, ct);

    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct)
        => inner.DeleteAsync(virtualPath, recursive, ct);

    public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct)
        => inner.WriteAllBytesAsync(virtualPath, content, ct);

    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct)
        => inner.AppendAllTextAsync(virtualPath, content, ct);

    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
        => inner.TryGetEntryAsync(virtualPath, ct);

    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default)
        => inner.OpenWriteStreamAsync(virtualPath, ct);

    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default)
        => inner.OpenAppendStreamAsync(virtualPath, ct);

    public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default)
        => inner.CopyAsync(srcVirtualPath, dstVirtualPath, overwrite, ct);
}
