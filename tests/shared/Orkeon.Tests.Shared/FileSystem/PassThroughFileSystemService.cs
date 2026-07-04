using System.Runtime.CompilerServices;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Tests.Shared.FileSystem;

/// <summary>
/// Test helper that treats every input path as both virtual <em>and</em> physical and
/// delegates I/O straight to <see cref="System.IO"/>. Used by legacy tool tests that
/// previously relied on <c>Mock&lt;IFileSystemService&gt;</c> with simple
/// "<c>Path.GetFullPath(path)</c>" responses. The optional <paramref name="pathValidator"/>
/// constructor argument lets tests plug in custom denial logic (e.g. traversal blocking).
/// </summary>
public sealed class PassThroughFileSystemService : IFileSystemService
{
    private readonly Func<string, FileAccessRights, PathValidationResult> _resolver;

    public PassThroughFileSystemService()
        : this((path, _) => PathValidationResult.Allowed(Path.GetFullPath(path)))
    {
    }

    public PassThroughFileSystemService(Func<string, FileAccessRights, PathValidationResult> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
        => _resolver(virtualPath, requiredRight);

    public string? ToVirtualPath(string physicalPath) => physicalPath;

    public IReadOnlyList<MountInfo> GetAvailableMounts() => Array.Empty<MountInfo>();

    public async IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
        string virtualRoot,
        VirtualEnumerationOptions? options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        options ??= new VirtualEnumerationOptions();
        var searchOption = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var pattern = options.SearchPattern ?? "*";

        if (!Directory.Exists(virtualRoot)) yield break;

        IEnumerable<string> files;
        IEnumerable<string> dirs;
        try
        {
            files = Directory.EnumerateFiles(virtualRoot, pattern, searchOption);
            dirs = Directory.EnumerateDirectories(virtualRoot, "*", searchOption);
        }
        catch { yield break; }

        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            FileInfo fi;
            try { fi = new FileInfo(f); } catch { continue; }
            yield return new VirtualFileEntry(f, fi.Length, fi.LastWriteTimeUtc, VirtualEntryKind.File);
        }
        foreach (var d in dirs)
        {
            ct.ThrowIfCancellationRequested();
            DirectoryInfo di;
            try { di = new DirectoryInfo(d); } catch { continue; }
            yield return new VirtualFileEntry(d, 0, di.LastWriteTimeUtc, VirtualEntryKind.Directory);
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Stream s = new FileStream(virtualPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return Task.FromResult(s);
    }

    public async Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
    {
        if (!File.Exists(virtualPath)) return null;
        return await File.ReadAllBytesAsync(virtualPath, ct).ConfigureAwait(false);
    }

    public async Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
    {
        if (!File.Exists(virtualPath)) return null;
        return await File.ReadAllTextAsync(virtualPath, ct).ConfigureAwait(false);
    }

    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Directory.Exists(virtualPath)) return Task.FromResult(VirtualEntryKind.Directory);
        if (File.Exists(virtualPath)) return Task.FromResult(VirtualEntryKind.File);
        throw new FileNotFoundException($"Path '{virtualPath}' not found.");
    }

    public async Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(virtualPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.WriteAllTextAsync(virtualPath, content, utf8NoBom, ct).ConfigureAwait(false);
        return utf8NoBom.GetByteCount(content);
    }

    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(virtualPath) || Directory.Exists(virtualPath));
    }

    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(virtualPath);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (File.Exists(virtualPath)) { File.Delete(virtualPath); return Task.FromResult(true); }
        if (Directory.Exists(virtualPath)) { Directory.Delete(virtualPath, recursive); return Task.FromResult(true); }
        return Task.FromResult(false);
    }

    public async Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var dir = Path.GetDirectoryName(virtualPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(virtualPath, content, ct).ConfigureAwait(false);
        return content.Length;
    }

    public async Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var dir = Path.GetDirectoryName(virtualPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.AppendAllTextAsync(virtualPath, content, utf8NoBom, ct).ConfigureAwait(false);
        return System.Text.Encoding.UTF8.GetByteCount(content);
    }

    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (File.Exists(virtualPath))
        {
            var fi = new FileInfo(virtualPath);
            return Task.FromResult<VirtualFileEntry?>(new VirtualFileEntry(virtualPath, fi.Length, fi.LastWriteTimeUtc, VirtualEntryKind.File));
        }
        if (Directory.Exists(virtualPath))
        {
            var di = new DirectoryInfo(virtualPath);
            return Task.FromResult<VirtualFileEntry?>(new VirtualFileEntry(virtualPath, 0, di.LastWriteTimeUtc, VirtualEntryKind.Directory));
        }
        return Task.FromResult<VirtualFileEntry?>(null);
    }

    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var dir = Path.GetDirectoryName(virtualPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        Stream s = new FileStream(virtualPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        return Task.FromResult(s);
    }

    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var dir = Path.GetDirectoryName(virtualPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        Stream s = new FileStream(virtualPath, FileMode.Append, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        return Task.FromResult(s);
    }

    public async Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(dstVirtualPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        if (!overwrite && File.Exists(dstVirtualPath))
            throw new IOException($"Destination file already exists: '{dstVirtualPath}'.");
        await using var src = new FileStream(srcVirtualPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        await using var dst = new FileStream(dstVirtualPath, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        await src.CopyToAsync(dst, 64 * 1024, ct).ConfigureAwait(false);
    }
}
