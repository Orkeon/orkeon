using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Test double for IFileSystemService backed by a real temp directory.
/// Supports any virtual path by treating it as a relative path under a configured root.
/// </summary>
public sealed class MockFileSystemService : IFileSystemService
{
    private readonly string _root;

    /// <summary>Creates a mock that maps virtual paths to files under <paramref name="root"/>.</summary>
    public MockFileSystemService(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private string ToPhysical(string virtualPath)
    {
        // Strip leading slash and combine with root
        var relative = virtualPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return string.IsNullOrEmpty(relative) ? _root : Path.Combine(_root, relative);
    }

    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
        => PathValidationResult.Allowed(ToPhysical(virtualPath));

    public string? ToVirtualPath(string physicalPath)
    {
        if (physicalPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            return "/" + physicalPath[_root.Length..].TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
        return null;
    }

    public IReadOnlyList<MountInfo> GetAvailableMounts()
        => [new MountInfo("/", FileAccessRights.ReadWrite | FileAccessRights.Create | FileAccessRights.Delete, [])];

    public async IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
        string virtualRoot, VirtualEnumerationOptions? options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var physicalRoot = ToPhysical(virtualRoot);
        if (!Directory.Exists(physicalRoot))
            yield break;

        var searchOption = options?.Recursive != false ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var pattern = options?.SearchPattern ?? "*";

        var files = Directory.GetFiles(physicalRoot, pattern, searchOption);
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var info = new FileInfo(f);
            var vPath = ToVirtualPath(f) ?? f;
            yield return new VirtualFileEntry(vPath, info.Length, info.LastWriteTimeUtc, VirtualEntryKind.File, info.CreationTimeUtc);
        }

        var dirs = Directory.GetDirectories(physicalRoot, "*", searchOption);
        foreach (var d in dirs)
        {
            ct.ThrowIfCancellationRequested();
            var info = new DirectoryInfo(d);
            var vPath = ToVirtualPath(d) ?? d;
            yield return new VirtualFileEntry(vPath, 0, info.LastWriteTimeUtc, VirtualEntryKind.Directory, info.CreationTimeUtc);
        }
    }

    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct)
    {
        var physical = ToPhysical(virtualPath);
        return Task.FromResult<Stream>(File.OpenRead(physical));
    }

    public async Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
    {
        var physical = ToPhysical(virtualPath);
        if (!File.Exists(physical))
            return null;
        return await File.ReadAllBytesAsync(physical, ct).ConfigureAwait(false);
    }

    public async Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
    {
        var physical = ToPhysical(virtualPath);
        if (!File.Exists(physical))
            return null;
        return await File.ReadAllTextAsync(physical, ct).ConfigureAwait(false);
    }

    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct)
    {
        var physical = ToPhysical(virtualPath);
        if (File.Exists(physical))
            return Task.FromResult(VirtualEntryKind.File);
        if (Directory.Exists(physical))
            return Task.FromResult(VirtualEntryKind.Directory);
        throw new FileNotFoundException($"Path not found: {virtualPath}", virtualPath);
    }

    public async Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
    {
        var physical = ToPhysical(virtualPath);
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        await File.WriteAllTextAsync(physical, content, ct).ConfigureAwait(false);
        return System.Text.Encoding.UTF8.GetByteCount(content);
    }

    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct)
    {
        var physical = ToPhysical(virtualPath);
        return Task.FromResult(File.Exists(physical) || Directory.Exists(physical));
    }

    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct)
    {
        Directory.CreateDirectory(ToPhysical(virtualPath));
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct)
    {
        var physical = ToPhysical(virtualPath);
        if (File.Exists(physical))
        {
            File.Delete(physical);
            return Task.FromResult(true);
        }
        if (Directory.Exists(physical))
        {
            Directory.Delete(physical, recursive);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public async Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct)
    {
        var physical = ToPhysical(virtualPath);
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        await File.WriteAllBytesAsync(physical, content, ct).ConfigureAwait(false);
        return content.Length;
    }

    public async Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct)
    {
        var physical = ToPhysical(virtualPath);
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        await File.AppendAllTextAsync(physical, content, ct).ConfigureAwait(false);
        return System.Text.Encoding.UTF8.GetByteCount(content);
    }

    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
    {
        var physical = ToPhysical(virtualPath);
        if (File.Exists(physical))
        {
            var info = new FileInfo(physical);
            return Task.FromResult<VirtualFileEntry?>(
                new VirtualFileEntry(virtualPath, info.Length, info.LastWriteTimeUtc, VirtualEntryKind.File, info.CreationTimeUtc));
        }
        if (Directory.Exists(physical))
        {
            var info = new DirectoryInfo(physical);
            return Task.FromResult<VirtualFileEntry?>(
                new VirtualFileEntry(virtualPath, 0, info.LastWriteTimeUtc, VirtualEntryKind.Directory, info.CreationTimeUtc));
        }
        return Task.FromResult<VirtualFileEntry?>(null);
    }

    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default)
    {
        var physical = ToPhysical(virtualPath);
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        return Task.FromResult<Stream>(File.Open(physical, FileMode.Create, FileAccess.Write));
    }

    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default)
    {
        var physical = ToPhysical(virtualPath);
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        return Task.FromResult<Stream>(File.Open(physical, FileMode.Append, FileAccess.Write));
    }

    public async Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default)
    {
        var srcPhysical = ToPhysical(srcVirtualPath);
        var dstPhysical = ToPhysical(dstVirtualPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dstPhysical)!);
        await Task.Run(() => File.Copy(srcPhysical, dstPhysical, overwrite), ct).ConfigureAwait(false);
    }
}
