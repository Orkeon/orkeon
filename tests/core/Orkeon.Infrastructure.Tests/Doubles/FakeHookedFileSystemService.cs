using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>A file system whose entry and reading of a file a test decides or observes.</summary>
public sealed class FakeHookedFileSystemService(FakeFileSystemService inner) : IFileSystemService
{
    /// <summary>The size the entry of a file announces, when it is not its own.</summary>
    public long? AnnouncedSize { get; set; }

    /// <summary>Called before a file's bytes are read.</summary>
    public Action? OnRead { get; set; }

    public async Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
    {
        var entry = await inner.TryGetEntryAsync(virtualPath, ct);
        return entry is not null && AnnouncedSize is { } size ? entry with { SizeBytes = size } : entry;
    }

    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
    {
        OnRead?.Invoke();
        return inner.TryReadAllBytesAsync(virtualPath, ct);
    }

    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight) => inner.ResolveAndValidate(virtualPath, requiredRight);

    public string? ToVirtualPath(string physicalPath) => inner.ToVirtualPath(physicalPath);

    public IReadOnlyList<MountInfo> GetAvailableMounts() => inner.GetAvailableMounts();

    public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct) =>
        inner.EnumerateFilesAsync(virtualRoot, options, ct);

    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct) => inner.OpenReadStreamAsync(virtualPath, ct);

    public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct) => inner.TryReadAllTextAsync(virtualPath, ct);

    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct) => inner.GetEntryKindAsync(virtualPath, ct);

    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct) => inner.WriteAllTextAsync(virtualPath, content, ct);

    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct) => inner.ExistsAsync(virtualPath, ct);

    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct) => inner.CreateDirectoryAsync(virtualPath, ct);

    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct) => inner.DeleteAsync(virtualPath, recursive, ct);

    public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct) => inner.WriteAllBytesAsync(virtualPath, content, ct);

    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct) => inner.AppendAllTextAsync(virtualPath, content, ct);

    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default) => inner.OpenWriteStreamAsync(virtualPath, ct);

    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default) => inner.OpenAppendStreamAsync(virtualPath, ct);

    public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default) =>
        inner.CopyAsync(srcVirtualPath, dstVirtualPath, overwrite, ct);
}
