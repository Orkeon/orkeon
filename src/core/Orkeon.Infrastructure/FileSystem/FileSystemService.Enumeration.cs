using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.FileSystemGlobbing;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Infrastructure.FileSystem;

/// <summary>
/// VFS v2.2 additions: enumeration and streaming APIs.
/// All disk access is encapsulated here — callers only see virtual paths.
/// </summary>
public sealed partial class FileSystemService
{
    /// <inheritdoc />
    public async IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
        string virtualRoot,
        VirtualEnumerationOptions? options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRoot);

        var physicalRoot = ResolveOrThrow(virtualRoot, FileAccessRights.Read);
        options ??= new VirtualEnumerationOptions();

        if (!Directory.Exists(physicalRoot))
            yield break;

        var matcher = BuildExcludeMatcher(options.Exclude);
        var maxDepth = options.MaxDepth ?? int.MaxValue;

        var enumOpts = new EnumerationOptions
        {
            RecurseSubdirectories = options.Recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = options.FollowSymlinks
                ? FileAttributes.None
                : FileAttributes.ReparsePoint,
            MaxRecursionDepth = maxDepth,
            ReturnSpecialDirectories = false
        };

        foreach (var physicalEntry in Directory.EnumerateFileSystemEntries(physicalRoot, "*", enumOpts))
        {
            ct.ThrowIfCancellationRequested();

            var relative = Path
                .GetRelativePath(physicalRoot, physicalEntry)
                .Replace(Path.DirectorySeparatorChar, '/');

            if (matcher is not null && !matcher.Match(relative).HasMatches)
                continue;

            if (options.SearchPattern is not null &&
                !System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                    options.SearchPattern, Path.GetFileName(physicalEntry), ignoreCase: true))
                continue;

            var virtualPath = ToVirtualPath(physicalEntry);
            if (virtualPath is null)
                continue;

            yield return BuildEntry(physicalEntry, virtualPath);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        ct.ThrowIfCancellationRequested();

        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Read);

        if (!File.Exists(physical))
            throw new FileNotFoundException(
                $"Virtual path '{virtualPath}' does not point to an existing file.");

        Stream stream = new FileStream(
            physical,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        ct.ThrowIfCancellationRequested();

        var v = ResolveAndValidate(virtualPath, FileAccessRights.Write | FileAccessRights.Create);
        if (!v.IsAllowed)
            throw new FileAccessDeniedException(v.DenialReason!, virtualPath, FileAccessRights.Write | FileAccessRights.Create);

        var physical = v.ResolvedPath!;
        var dir = Path.GetDirectoryName(physical);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        Stream stream = new FileStream(physical, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        ct.ThrowIfCancellationRequested();

        var v = ResolveAndValidate(virtualPath, FileAccessRights.Write | FileAccessRights.Create);
        if (!v.IsAllowed)
            throw new FileAccessDeniedException(v.DenialReason!, virtualPath, FileAccessRights.Write | FileAccessRights.Create);

        var physical = v.ResolvedPath!;
        var dir = Path.GetDirectoryName(physical);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        Stream stream = new FileStream(physical, FileMode.Append, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public async Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(srcVirtualPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(dstVirtualPath);

        var srcV = ResolveAndValidate(srcVirtualPath, FileAccessRights.Read);
        if (!srcV.IsAllowed)
            throw new FileAccessDeniedException(srcV.DenialReason!, srcVirtualPath, FileAccessRights.Read);

        var dstV = ResolveAndValidate(dstVirtualPath, FileAccessRights.Write | FileAccessRights.Create);
        if (!dstV.IsAllowed)
            throw new FileAccessDeniedException(dstV.DenialReason!, dstVirtualPath, FileAccessRights.Write | FileAccessRights.Create);

        var srcPhysical = srcV.ResolvedPath!;
        var dstPhysical = dstV.ResolvedPath!;

        if (!overwrite && File.Exists(dstPhysical))
            throw new IOException($"Destination file already exists: '{dstVirtualPath}'.");

        var dstDir = Path.GetDirectoryName(dstPhysical);
        if (!string.IsNullOrEmpty(dstDir) && !Directory.Exists(dstDir))
            Directory.CreateDirectory(dstDir);

        // Intra-mount fast path: check if both physical paths are under the same mount
        var srcVirtual = ToVirtualPath(srcPhysical);
        var dstVirtual = ToVirtualPath(dstPhysical);
        var srcMountRoot = srcVirtual is not null ? GetMountVirtualRoot(srcVirtual) : null;
        var dstMountRoot = dstVirtual is not null ? GetMountVirtualRoot(dstVirtual) : null;

        if (srcMountRoot is not null && string.Equals(srcMountRoot, dstMountRoot, StringComparison.Ordinal))
        {
            File.Copy(srcPhysical, dstPhysical, overwrite);
        }
        else
        {
            const int bufferSize = 64 * 1024;
            // Explicit try/finally DisposeAsync (not the repo's usual `await using var __x =
            // stream.ConfigureAwait(false)` idiom): SonarQube's S2930 does not track disposal
            // through ConfiguredAsyncDisposable and reported srcStream as leaked.
            var srcStream = new FileStream(srcPhysical, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
            try
            {
                var dstStream = new FileStream(dstPhysical, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
                try
                {
                    await srcStream.CopyToAsync(dstStream, bufferSize, ct).ConfigureAwait(false);
                }
                finally
                {
                    await dstStream.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await srcStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private string? GetMountVirtualRoot(string virtualPath)
    {
        return GetAvailableMounts()
            .FirstOrDefault(mount =>
                string.Equals(virtualPath, mount.VirtualPath, StringComparison.Ordinal) ||
                virtualPath.StartsWith(mount.VirtualPath + "/", StringComparison.Ordinal))
            ?.VirtualPath;
    }

    /// <inheritdoc />
    public async Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Read);

        if (!File.Exists(physical))
            return null;

        return await File.ReadAllBytesAsync(physical, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Read);

        if (!File.Exists(physical))
            return null;

        return await File.ReadAllTextAsync(physical, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        ArgumentNullException.ThrowIfNull(content);
        return WriteAllTextCoreAsync(virtualPath, content, ct);
    }

    private async Task<int> WriteAllTextCoreAsync(string virtualPath, string content, CancellationToken ct)
    {
        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Write | FileAccessRights.Create);

        var dir = Path.GetDirectoryName(physical);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.WriteAllTextAsync(physical, content, utf8NoBom, ct).ConfigureAwait(false);
        return utf8NoBom.GetByteCount(content);
    }

    /// <inheritdoc />
    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        ct.ThrowIfCancellationRequested();

        var physical = ResolveOrThrow(virtualPath, FileAccessRights.Read);

        if (!File.Exists(physical) && !Directory.Exists(physical))
            throw new FileNotFoundException(
                $"Virtual path '{virtualPath}' does not point to an existing entry.");

        return Task.FromResult(ResolveKind(physical));
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        ct.ThrowIfCancellationRequested();

        var v = ResolveAndValidate(virtualPath, FileAccessRights.Create);
        if (!v.IsAllowed)
            throw new FileAccessDeniedException(
                v.DenialReason ?? "Create denied", virtualPath, FileAccessRights.Create);

        Directory.CreateDirectory(v.ResolvedPath!);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        ct.ThrowIfCancellationRequested();

        var v = ResolveAndValidate(virtualPath, FileAccessRights.Delete);
        if (!v.IsAllowed)
            throw new FileAccessDeniedException(
                v.DenialReason ?? "Delete denied", virtualPath, FileAccessRights.Delete);

        var physical = v.ResolvedPath!;

        if (File.Exists(physical))
        {
            File.Delete(physical);
            return Task.FromResult(true);
        }

        if (Directory.Exists(physical))
        {
            if (recursive)
                EnsureNoOutOfMountSymlinks(physical);

            Directory.Delete(physical, recursive);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private void EnsureNoOutOfMountSymlinks(string physicalDir)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(physicalDir, "*",
            new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.None, ReturnSpecialDirectories = false }))
        {
            var attrs = File.GetAttributes(entry);
            if ((attrs & FileAttributes.ReparsePoint) == 0) continue;

            var virtualTarget = ToVirtualPath(entry);
            if (virtualTarget is null)
                throw new FileAccessDeniedException(
                    "Recursive delete aborted: subtree contains a symlink pointing outside all mounts.",
                    physicalDir, FileAccessRights.Delete);
        }
    }

    /// <inheritdoc />
    public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        ArgumentNullException.ThrowIfNull(content);
        return WriteAllBytesCoreAsync(virtualPath, content, ct);
    }

    private async Task<int> WriteAllBytesCoreAsync(string virtualPath, byte[] content, CancellationToken ct)
    {
        var v = ResolveAndValidate(virtualPath, FileAccessRights.Write | FileAccessRights.Create);
        if (!v.IsAllowed) throw new FileAccessDeniedException(v.DenialReason!, virtualPath, FileAccessRights.Write | FileAccessRights.Create);

        var physical = v.ResolvedPath!;
        var dir = Path.GetDirectoryName(physical);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(physical, content, ct).ConfigureAwait(false);
        return content.Length;
    }

    /// <inheritdoc />
    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        ArgumentNullException.ThrowIfNull(content);
        return AppendAllTextCoreAsync(virtualPath, content, ct);
    }

    private async Task<int> AppendAllTextCoreAsync(string virtualPath, string content, CancellationToken ct)
    {
        var v = ResolveAndValidate(virtualPath, FileAccessRights.Write | FileAccessRights.Create);
        if (!v.IsAllowed) throw new FileAccessDeniedException(v.DenialReason!, virtualPath, FileAccessRights.Write | FileAccessRights.Create);

        var physical = v.ResolvedPath!;
        var dir = Path.GetDirectoryName(physical);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.AppendAllTextAsync(physical, content, utf8NoBom, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetByteCount(content);
    }

    /// <inheritdoc />
    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        ct.ThrowIfCancellationRequested();

        var v = ResolveAndValidate(virtualPath, FileAccessRights.Read);
        if (!v.IsAllowed) return Task.FromResult<VirtualFileEntry?>(null);

        var physical = v.ResolvedPath!;

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

    private string ResolveOrThrow(string virtualPath, FileAccessRights required)
    {
        var validation = ResolveAndValidate(virtualPath, required);
        if (!validation.IsAllowed)
            throw new FileAccessDeniedException(
                validation.DenialReason ?? $"Access denied for virtual path '{virtualPath}'.",
                virtualPath,
                required);

        return validation.ResolvedPath!;
    }

    private static Matcher? BuildExcludeMatcher(IReadOnlyList<string>? excludes)
    {
        if (excludes is null || excludes.Count == 0)
            return null;

        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude("**/*");
        foreach (var pattern in excludes)
        {
            if (!string.IsNullOrWhiteSpace(pattern))
                matcher.AddExclude(pattern);
        }

        return matcher;
    }

    private static VirtualFileEntry BuildEntry(string physicalPath, string virtualPath)
    {
        var kind = ResolveKind(physicalPath);

        long size = 0;
        DateTimeOffset lastMod;

        if (kind == VirtualEntryKind.File)
        {
            var fi = new FileInfo(physicalPath);
            size = fi.Length;
            lastMod = fi.LastWriteTimeUtc;
        }
        else
        {
            var di = new DirectoryInfo(physicalPath);
            lastMod = di.LastWriteTimeUtc;
        }

        return new VirtualFileEntry(virtualPath, size, lastMod, kind);
    }

    private static VirtualEntryKind ResolveKind(string physicalPath)
    {
        var attrs = File.GetAttributes(physicalPath);

        if ((attrs & FileAttributes.ReparsePoint) != 0)
            return VirtualEntryKind.SymLink;

        if ((attrs & FileAttributes.Directory) != 0)
            return VirtualEntryKind.Directory;

        return VirtualEntryKind.File;
    }
}
