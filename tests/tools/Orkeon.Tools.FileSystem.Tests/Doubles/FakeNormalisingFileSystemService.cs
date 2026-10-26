using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Tools.FileSystem.Tests.Doubles;

/// <summary>Delegating decorator that collapses "." segments and duplicate "/" of every virtual path like Path.GetFullPath does in the real VFS.</summary>
public sealed class FakeNormalisingFileSystemService(IFileSystemService inner) : IFileSystemService
{
    private static string N(string p) =>
        "/" + string.Join('/', p.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => s != "."));

    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
    {
        return inner.TryReadAllBytesAsync(N(virtualPath), ct);
    }

    public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
    {
        return inner.TryReadAllTextAsync(N(virtualPath), ct);
    }

    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct)
    {
        return inner.OpenReadStreamAsync(N(virtualPath), ct);
    }

    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
        => inner.ResolveAndValidate(N(virtualPath), requiredRight);

    public string? ToVirtualPath(string physicalPath) => inner.ToVirtualPath(physicalPath);

    public string? WorkingDirectory => inner.WorkingDirectory;
    public string? WriteDirectory => inner.WriteDirectory;

    public IReadOnlyList<MountInfo> GetAvailableMounts() => inner.GetAvailableMounts();

    public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
        string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct)
        => inner.EnumerateFilesAsync(N(virtualRoot), options, ct);

    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct)
        => inner.GetEntryKindAsync(N(virtualPath), ct);

    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
        => inner.WriteAllTextAsync(N(virtualPath), content, ct);

    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct)
        => inner.ExistsAsync(N(virtualPath), ct);

    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct)
        => inner.CreateDirectoryAsync(N(virtualPath), ct);

    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct)
        => inner.DeleteAsync(N(virtualPath), recursive, ct);

    public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct)
        => inner.WriteAllBytesAsync(N(virtualPath), content, ct);

    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct)
        => inner.AppendAllTextAsync(N(virtualPath), content, ct);

    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
        => inner.TryGetEntryAsync(N(virtualPath), ct);

    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default)
        => inner.OpenWriteStreamAsync(N(virtualPath), ct);

    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default)
        => inner.OpenAppendStreamAsync(N(virtualPath), ct);

    public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default)
        => inner.CopyAsync(N(srcVirtualPath), N(dstVirtualPath), overwrite, ct);
}
