using System.Runtime.CompilerServices;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Tests.Shared.FileSystem;

/// <summary>
/// Test helper implementation of <see cref="IFileSystemService"/> that maps a single
/// physical directory to a single virtual path. Useful for integration tests that need
/// real disk I/O (e.g. Tree-sitter parsers) but still exercise the virtual-path API.
/// </summary>
public sealed class DiskBackedFileSystemService : IFileSystemService
{
    private readonly string _physicalBase;
    private readonly string _virtualRoot;
    private readonly MountInfo _mountInfo;

    public DiskBackedFileSystemService(string physicalBase, string virtualRoot = "/src")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalBase);
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRoot);
        if (!virtualRoot.StartsWith('/'))
            throw new ArgumentException($"Virtual root must start with '/': '{virtualRoot}'", nameof(virtualRoot));

        _physicalBase = Path.GetFullPath(physicalBase);
        _virtualRoot = virtualRoot.TrimEnd('/');
        _mountInfo = new MountInfo(_virtualRoot, FileAccessRights.ReadWrite, Array.Empty<SubPathOverride>());
    }

    public string VirtualRoot => _virtualRoot;
    public string PhysicalBase => _physicalBase;

    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        var physical = TryResolve(virtualPath);
        if (physical is null)
            return PathValidationResult.Denied($"No mount for virtual path '{virtualPath}'.");

        // Containment guard
        var resolved = Path.GetFullPath(physical);
        if (!string.Equals(resolved, _physicalBase, StringComparison.Ordinal) &&
            !resolved.StartsWith(_physicalBase + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return PathValidationResult.Denied($"Path traversal detected for '{virtualPath}'.");

        return PathValidationResult.Allowed(resolved);
    }

    public string? ToVirtualPath(string physicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalPath);
        var resolved = Path.GetFullPath(physicalPath);
        if (string.Equals(resolved, _physicalBase, StringComparison.Ordinal))
            return _virtualRoot;
        if (resolved.StartsWith(_physicalBase + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            var rel = resolved[(_physicalBase.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
            return $"{_virtualRoot}/{rel}";
        }
        return null;
    }

    public IReadOnlyList<MountInfo> GetAvailableMounts() => [_mountInfo];

    public async IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
        string virtualRoot,
        VirtualEnumerationOptions? options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var physicalRoot = ResolveOrThrow(virtualRoot, FileAccessRights.Read);
        options ??= new VirtualEnumerationOptions();

        if (!Directory.Exists(physicalRoot)) yield break;

        var enumOpts = new EnumerationOptions
        {
            RecurseSubdirectories = options.Recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = options.FollowSymlinks ? FileAttributes.None : FileAttributes.ReparsePoint,
            MaxRecursionDepth = options.MaxDepth ?? int.MaxValue,
            ReturnSpecialDirectories = false,
        };

        foreach (var physical in Directory.EnumerateFileSystemEntries(physicalRoot, "*", enumOpts))
        {
            ct.ThrowIfCancellationRequested();

            if (options.SearchPattern is not null &&
                !System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                    options.SearchPattern, Path.GetFileName(physical), ignoreCase: true))
                continue;

            var vp = ToVirtualPath(physical);
            if (vp is null) continue;

            var kind = ResolveKind(physical);
            long size = 0;
            DateTimeOffset lastMod;
            if (kind == VirtualEntryKind.File)
            {
                var fi = new FileInfo(physical);
                size = fi.Length;
                lastMod = fi.LastWriteTimeUtc;
            }
            else
            {
                lastMod = new DirectoryInfo(physical).LastWriteTimeUtc;
            }

            yield return new VirtualFileEntry(vp, size, lastMod, kind);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(string virtualPath, CancellationToken ct)
    {
        try
        {
            _ = await GetEntryKindAsync(virtualPath, ct).ConfigureAwait(false);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (FileAccessDeniedException) { return false; }
    }

    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        ct.ThrowIfCancellationRequested();
        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Create);
        Directory.CreateDirectory(physical);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        ct.ThrowIfCancellationRequested();
        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Delete);

        if (File.Exists(physical))
        {
            File.Delete(physical);
            return Task.FromResult(true);
        }

        if (Directory.Exists(physical))
        {
            if (recursive)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(physical, "*",
                    new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.None, ReturnSpecialDirectories = false }))
                {
                    var attrs = File.GetAttributes(entry);
                    if ((attrs & FileAttributes.ReparsePoint) != 0 && ToVirtualPath(entry) is null)
                        throw new FileAccessDeniedException(
                            "Recursive delete aborted: subtree contains a symlink pointing outside all mounts.",
                            virtualPath, FileAccessRights.Delete);
                }
            }

            Directory.Delete(physical, recursive);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Read);
        if (!File.Exists(physical))
            throw new FileNotFoundException($"Virtual path '{virtualPath}' not found.");

        Stream stream = new FileStream(physical, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Write | FileAccessRights.Create);
        var dir = Path.GetDirectoryName(physical);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        Stream stream = new FileStream(physical, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Write | FileAccessRights.Create);
        var dir = Path.GetDirectoryName(physical);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        Stream stream = new FileStream(physical, FileMode.Append, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        return Task.FromResult(stream);
    }

    public async Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default)
    {
        var srcPhysical = ResolveOrThrow(srcVirtualPath, FileAccessRights.Read);
        var dstPhysical = ResolveOrThrow(dstVirtualPath, FileAccessRights.Write | FileAccessRights.Create);

        if (!overwrite && File.Exists(dstPhysical))
            throw new IOException($"Destination file already exists: '{dstVirtualPath}'.");

        var dstDir = Path.GetDirectoryName(dstPhysical);
        if (!string.IsNullOrEmpty(dstDir) && !Directory.Exists(dstDir))
            Directory.CreateDirectory(dstDir);

        // Same physical base = intra-mount
        if (string.Equals(_physicalBase, GetPhysicalBaseFor(srcPhysical), StringComparison.Ordinal) &&
            string.Equals(_physicalBase, GetPhysicalBaseFor(dstPhysical), StringComparison.Ordinal))
        {
            File.Copy(srcPhysical, dstPhysical, overwrite);
        }
        else
        {
            const int bufferSize = 64 * 1024;
            await using var srcStream = new FileStream(srcPhysical, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
            await using var dstStream = new FileStream(dstPhysical, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
            await srcStream.CopyToAsync(dstStream, bufferSize, ct).ConfigureAwait(false);
        }
    }

    private string GetPhysicalBaseFor(string physicalPath) =>
        physicalPath.StartsWith(_physicalBase + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
        string.Equals(physicalPath, _physicalBase, StringComparison.Ordinal)
            ? _physicalBase
            : string.Empty;

    public async Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
    {
        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Read);
        if (!File.Exists(physical)) return null;
        return await File.ReadAllBytesAsync(physical, ct).ConfigureAwait(false);
    }

    public async Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
    {
        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Read);
        if (!File.Exists(physical)) return null;
        return await File.ReadAllTextAsync(physical, ct).ConfigureAwait(false);
    }

    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Read);
        if (!File.Exists(physical) && !Directory.Exists(physical))
            throw new FileNotFoundException($"Virtual path '{virtualPath}' not found.");
        return Task.FromResult(ResolveKind(physical));
    }

    public async Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
    {
        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Write | FileAccessRights.Create);
        var dir = Path.GetDirectoryName(physical);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.WriteAllTextAsync(physical, content, utf8NoBom, ct).ConfigureAwait(false);
        return utf8NoBom.GetByteCount(content);
    }

    public async Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Write | FileAccessRights.Create);
        var dir = Path.GetDirectoryName(physical);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(physical, content, ct).ConfigureAwait(false);
        return content.Length;
    }

    public async Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Write | FileAccessRights.Create);
        var dir = Path.GetDirectoryName(physical);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.AppendAllTextAsync(physical, content, utf8NoBom, ct).ConfigureAwait(false);
        return System.Text.Encoding.UTF8.GetByteCount(content);
    }

    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var validation = ResolveAndValidate(virtualPath, FileAccessRights.Read);
        if (!validation.IsAllowed) return Task.FromResult<VirtualFileEntry?>(null);
        var physical = validation.ResolvedPath!;

        if (File.Exists(physical))
        {
            var fi = new FileInfo(physical);
            var linkTarget = fi.LinkTarget is null ? null : ToVirtualPath(fi.LinkTarget) ?? $"ext::{fi.LinkTarget}";
            return Task.FromResult<VirtualFileEntry?>(new VirtualFileEntry(
                virtualPath,
                fi.Length,
                fi.LastWriteTimeUtc,
                fi.LinkTarget != null ? VirtualEntryKind.SymLink : VirtualEntryKind.File,
                fi.CreationTimeUtc,
                linkTarget));
        }

        if (Directory.Exists(physical))
        {
            var di = new DirectoryInfo(physical);
            var linkTarget = di.LinkTarget is null ? null : ToVirtualPath(di.LinkTarget) ?? $"ext::{di.LinkTarget}";
            return Task.FromResult<VirtualFileEntry?>(new VirtualFileEntry(
                virtualPath,
                0,
                di.LastWriteTimeUtc,
                di.LinkTarget != null ? VirtualEntryKind.SymLink : VirtualEntryKind.Directory,
                di.CreationTimeUtc,
                linkTarget));
        }

        return Task.FromResult<VirtualFileEntry?>(null);
    }

    private string? TryResolve(string virtualPath)
    {
        if (string.Equals(virtualPath, _virtualRoot, StringComparison.Ordinal))
            return _physicalBase;
        if (virtualPath.StartsWith(_virtualRoot + "/", StringComparison.Ordinal))
        {
            var rel = virtualPath[(_virtualRoot.Length + 1)..];
            return Path.Combine(_physicalBase, rel.Replace('/', Path.DirectorySeparatorChar));
        }
        return null;
    }

    private string ResolveOrThrow(string virtualPath, FileAccessRights right)
    {
        var validation = ResolveAndValidate(virtualPath, right);
        if (!validation.IsAllowed)
            throw new FileAccessDeniedException(
                validation.DenialReason ?? "Access denied", virtualPath, right);
        return validation.ResolvedPath!;
    }

    private static VirtualEntryKind ResolveKind(string physical)
    {
        var attrs = File.GetAttributes(physical);
        if ((attrs & FileAttributes.ReparsePoint) != 0) return VirtualEntryKind.SymLink;
        if ((attrs & FileAttributes.Directory) != 0) return VirtualEntryKind.Directory;
        return VirtualEntryKind.File;
    }
}
