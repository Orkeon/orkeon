using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.ConsoleApp.Tests.Fakes;

/// <summary>Minimal in-memory file system fake for unit tests.</summary>
public sealed class FakeFileSystemService : IFileSystemService
{
    private readonly HashSet<string> _existingPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    public void AddFile(string path, string content = "")
    {
        _existingPaths.Add(path);
        _files[path] = content;
    }

    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct)
        => Task.FromResult(_existingPaths.Contains(virtualPath));

    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct)
    {
        _existingPaths.Add(virtualPath);
        return Task.CompletedTask;
    }

    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
    {
        _existingPaths.Add(virtualPath);
        _files[virtualPath] = content;
        return Task.FromResult(System.Text.Encoding.UTF8.GetByteCount(content));
    }

    public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
        => Task.FromResult(_files.TryGetValue(virtualPath, out var c) ? c : null);

    // ─── unused stubs ───
    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight) => throw new NotImplementedException();
    public string? ToVirtualPath(string physicalPath) => throw new NotImplementedException();
    public IReadOnlyList<MountInfo> GetAvailableMounts() => Array.Empty<MountInfo>();
    public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct) => throw new NotImplementedException();
    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct) => throw new NotImplementedException();
    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct) => throw new NotImplementedException();
    public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct) => throw new NotImplementedException();
    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct) => throw new NotImplementedException();
    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct) => Task.FromResult<VirtualFileEntry?>(null);
    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default) => throw new NotImplementedException();
    public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default) => throw new NotImplementedException();
}
