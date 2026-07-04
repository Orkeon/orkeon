using Orkeon.Compliance.Vfs;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Scripting.Cli;

/// <summary>
/// Minimal <see cref="IFileSystemService"/> for the CLI entrypoint. Maps a single host
/// directory (the script's parent folder) to a <c>/script</c> virtual root. Read-only:
/// the CLI only loads scripts, it doesn't write back.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: CLI entrypoint loads a user-supplied script from disk before any VFS mounts exist.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via Activator.CreateInstance (reflective construction in Orkeon.Scripting.Cli.Tests against the internal type), which the analyzer cannot observe.")]
internal sealed class CliFileSystemService : IFileSystemService
{
    private readonly string _physicalBase;
    private readonly string _virtualRoot;

    public CliFileSystemService(string physicalBase, string virtualRoot = "/script")
    {
        _physicalBase = Path.GetFullPath(physicalBase);
        _virtualRoot = virtualRoot.TrimEnd('/');
    }

    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
    {
        if (!virtualPath.StartsWith(_virtualRoot, StringComparison.Ordinal))
            return PathValidationResult.Denied($"Path '{virtualPath}' outside CLI mount.");
        var relative = virtualPath[_virtualRoot.Length..].TrimStart('/');
        var absolute = Path.GetFullPath(Path.Combine(_physicalBase, relative));
        if (!absolute.StartsWith(_physicalBase, StringComparison.Ordinal))
            return PathValidationResult.Denied("Path traversal detected.");
        return PathValidationResult.Allowed(absolute);
    }

    public string? ToVirtualPath(string physicalPath)
    {
        var resolved = Path.GetFullPath(physicalPath);
        if (resolved.StartsWith(_physicalBase, StringComparison.Ordinal))
        {
            var rel = resolved.Substring(_physicalBase.Length).TrimStart(Path.DirectorySeparatorChar)
                .Replace(Path.DirectorySeparatorChar, '/');
            return string.IsNullOrEmpty(rel) ? _virtualRoot : $"{_virtualRoot}/{rel}";
        }
        return null;
    }

    public IReadOnlyList<MountInfo> GetAvailableMounts()
        => [new MountInfo(_virtualRoot, FileAccessRights.Read, Array.Empty<SubPathOverride>())];

    public async Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
    {
        var v = ResolveAndValidate(virtualPath, FileAccessRights.Read);
        if (!v.IsAllowed || v.ResolvedPath is null) return null;
        if (!File.Exists(v.ResolvedPath)) return null;
        return await File.ReadAllTextAsync(v.ResolvedPath, ct).ConfigureAwait(false);
    }

    // Other surface kept minimal: the CLI only uses TryReadAllTextAsync.
    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
        => throw new NotSupportedException("CLI file system supports text reads only.");
    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct)
        => throw new NotSupportedException();
    public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct)
        => throw new NotSupportedException();
    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct)
        => throw new NotSupportedException();
    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
        => throw new NotSupportedException();
    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct)
        => throw new NotSupportedException();
    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct)
        => throw new NotSupportedException();
    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct)
        => throw new NotSupportedException();
    public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct)
        => throw new NotSupportedException();
    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct)
        => throw new NotSupportedException();
    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
        => throw new NotSupportedException();
    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default)
        => throw new NotSupportedException();
    public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default)
        => throw new NotSupportedException();
}
