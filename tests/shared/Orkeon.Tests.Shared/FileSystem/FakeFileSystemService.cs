using System.Runtime.CompilerServices;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Tests.Shared.FileSystem;

/// <summary>
/// In-memory <see cref="IFileSystemService"/> implementation for unit tests.
/// Stores files keyed by virtual path — no disk I/O involved.
/// </summary>
public sealed class FakeFileSystemService : IFileSystemService
{
    private readonly Dictionary<string, FakeEntry> _entries = new(StringComparer.Ordinal);
    private readonly List<MountInfo> _mounts = [];

    public FakeFileSystemService AddMount(string virtualPath, FileAccessRights rights = FileAccessRights.ReadOnly)
    {
        _mounts.Add(new MountInfo(virtualPath, rights, Array.Empty<SubPathOverride>()));
        return this;
    }

    public FakeFileSystemService AddFile(string virtualPath, string content, DateTimeOffset? lastModified = null)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        _entries[virtualPath] = new FakeEntry(bytes, lastModified ?? DateTimeOffset.UtcNow, VirtualEntryKind.File);
        EnsureDirectories(virtualPath);
        return this;
    }

    public FakeFileSystemService AddFile(string virtualPath, byte[] bytes, DateTimeOffset? lastModified = null)
    {
        _entries[virtualPath] = new FakeEntry(bytes, lastModified ?? DateTimeOffset.UtcNow, VirtualEntryKind.File);
        EnsureDirectories(virtualPath);
        return this;
    }

    public FakeFileSystemService AddDirectory(string virtualPath, DateTimeOffset? lastModified = null)
    {
        _entries[virtualPath] = new FakeEntry([], lastModified ?? DateTimeOffset.UtcNow, VirtualEntryKind.Directory);
        return this;
    }

    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);

        if (!IsUnderMount(virtualPath))
            return PathValidationResult.Denied($"No mount for virtual path '{virtualPath}'.");

        // FakeFileSystemService treats virtual path as its own resolved value to avoid leaking a physical path.
        return PathValidationResult.Allowed(virtualPath);
    }

    public string? ToVirtualPath(string physicalPath)
    {
        return _entries.ContainsKey(physicalPath) ? physicalPath : null;
    }

    public IReadOnlyList<MountInfo> GetAvailableMounts() => _mounts.AsReadOnly();

    public async IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
        string virtualRoot,
        VirtualEnumerationOptions? options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualRoot);
        options ??= new VirtualEnumerationOptions();

        if (!IsUnderMount(virtualRoot))
            throw new FileAccessDeniedException(
                $"No mount for virtual path '{virtualRoot}'.", virtualRoot, FileAccessRights.Read);

        var prefix = virtualRoot.TrimEnd('/') + "/";

        foreach (var (path, entry) in _entries.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            if (!path.StartsWith(prefix, StringComparison.Ordinal) &&
                !string.Equals(path, virtualRoot, StringComparison.Ordinal))
                continue;

            var relative = string.Equals(path, virtualRoot, StringComparison.Ordinal)
                ? string.Empty
                : path[prefix.Length..];

            if (!options.Recursive && relative.Contains('/')) continue;
            if (options.MaxDepth is int maxDepth && CountSegments(relative) > maxDepth) continue;

            if (options.SearchPattern is not null)
            {
                var name = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
                if (!System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                        options.SearchPattern, name, ignoreCase: true))
                    continue;
            }

            yield return new VirtualFileEntry(path, entry.Bytes.Length, entry.LastModified, entry.Kind);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureUnderMount(virtualPath);

        if (!_entries.TryGetValue(virtualPath, out var entry) || entry.Kind != VirtualEntryKind.File)
            throw new FileNotFoundException($"Virtual file '{virtualPath}' not found.");

        Stream ms = new MemoryStream(entry.Bytes, writable: false);
        return Task.FromResult(ms);
    }

    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
    {
        EnsureUnderMount(virtualPath);
        if (!_entries.TryGetValue(virtualPath, out var entry) || entry.Kind != VirtualEntryKind.File)
            return Task.FromResult<byte[]?>(null);
        return Task.FromResult<byte[]?>(entry.Bytes);
    }

    public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
    {
        EnsureUnderMount(virtualPath);
        if (!_entries.TryGetValue(virtualPath, out var entry) || entry.Kind != VirtualEntryKind.File)
            return Task.FromResult<string?>(null);
        return Task.FromResult<string?>(System.Text.Encoding.UTF8.GetString(entry.Bytes));
    }

    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct)
    {
        EnsureUnderMount(virtualPath);
        if (!_entries.TryGetValue(virtualPath, out var entry))
            throw new FileNotFoundException($"Virtual path '{virtualPath}' not found.");
        return Task.FromResult(entry.Kind);
    }

    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
    {
        EnsureUnderMount(virtualPath);
        EnsureDirectories(virtualPath);
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        _entries[virtualPath] = new FakeEntry(bytes, DateTimeOffset.UtcNow, VirtualEntryKind.File, DateTimeOffset.UtcNow);
        return Task.FromResult(bytes.Length);
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
        EnsureUnderMount(virtualPath);
        if (!_entries.ContainsKey(virtualPath))
            _entries[virtualPath] = new FakeEntry([], DateTimeOffset.UtcNow, VirtualEntryKind.Directory);
        EnsureDirectories(virtualPath + "/placeholder");
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        EnsureUnderMount(virtualPath);

        if (!_entries.TryGetValue(virtualPath, out var entry))
            return Task.FromResult(false);

        if (entry.Kind == VirtualEntryKind.Directory && recursive)
        {
            var prefix = virtualPath.TrimEnd('/') + "/";
            var toRemove = _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var key in toRemove)
                _entries.Remove(key);
        }

        _entries.Remove(virtualPath);
        return Task.FromResult(true);
    }

    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureUnderMount(virtualPath);
        EnsureDirectories(virtualPath);
        Stream stream = new CommitOnDisposeStream(this, virtualPath, append: false);
        return Task.FromResult(stream);
    }

    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureUnderMount(virtualPath);
        EnsureDirectories(virtualPath);
        // Seed from existing content so the stream starts at the right position
        var existing = _entries.TryGetValue(virtualPath, out var e) && e.Kind == VirtualEntryKind.File
            ? e.Bytes : Array.Empty<byte>();
        Stream stream = new CommitOnDisposeStream(this, virtualPath, append: true, existing);
        return Task.FromResult(stream);
    }

    public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        EnsureUnderMount(srcVirtualPath);
        EnsureUnderMount(dstVirtualPath);

        if (!_entries.TryGetValue(srcVirtualPath, out var src) || src.Kind != VirtualEntryKind.File)
            throw new FileNotFoundException($"Virtual file '{srcVirtualPath}' not found.");

        if (!overwrite && _entries.ContainsKey(dstVirtualPath))
            throw new IOException($"Destination file already exists: '{dstVirtualPath}'.");

        EnsureDirectories(dstVirtualPath);
        _entries[dstVirtualPath] = new FakeEntry(src.Bytes.ToArray(), DateTimeOffset.UtcNow, VirtualEntryKind.File, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        EnsureUnderMount(virtualPath);
        EnsureDirectories(virtualPath);
        _entries[virtualPath] = new FakeEntry(content, DateTimeOffset.UtcNow, VirtualEntryKind.File, DateTimeOffset.UtcNow);
        return Task.FromResult(content.Length);
    }

    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        EnsureUnderMount(virtualPath);
        EnsureDirectories(virtualPath);
        var newBytes = System.Text.Encoding.UTF8.GetBytes(content);
        if (_entries.TryGetValue(virtualPath, out var existing) && existing.Kind == VirtualEntryKind.File)
        {
            var combined = existing.Bytes.Concat(newBytes).ToArray();
            _entries[virtualPath] = existing with { Bytes = combined, LastModified = DateTimeOffset.UtcNow };
        }
        else
        {
            _entries[virtualPath] = new FakeEntry(newBytes, DateTimeOffset.UtcNow, VirtualEntryKind.File, DateTimeOffset.UtcNow);
        }
        return Task.FromResult(newBytes.Length);
    }

    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        if (!IsUnderMount(virtualPath)) return Task.FromResult<VirtualFileEntry?>(null);
        if (!_entries.TryGetValue(virtualPath, out var entry)) return Task.FromResult<VirtualFileEntry?>(null);
        return Task.FromResult<VirtualFileEntry?>(new VirtualFileEntry(
            virtualPath,
            entry.Bytes.Length,
            entry.LastModified,
            entry.Kind,
            entry.CreationTime,
            null));
    }

    private void EnsureDirectories(string virtualPath)
    {
        var parts = virtualPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            current = current.Length == 0 ? "/" + parts[i] : current + "/" + parts[i];
            if (!_entries.ContainsKey(current))
                _entries[current] = new FakeEntry([], DateTimeOffset.UtcNow, VirtualEntryKind.Directory);
        }
    }

    private bool IsUnderMount(string virtualPath)
    {
        if (_mounts.Count == 0) return true; // permissive for simple tests
        foreach (var mount in _mounts)
        {
            if (string.Equals(virtualPath, mount.VirtualPath, StringComparison.Ordinal)) return true;
            if (virtualPath.StartsWith(mount.VirtualPath + "/", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private void EnsureUnderMount(string virtualPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        if (!IsUnderMount(virtualPath))
            throw new FileAccessDeniedException(
                $"No mount for virtual path '{virtualPath}'.", virtualPath, FileAccessRights.Read);
    }

    private static int CountSegments(string relative) =>
        relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;

    private readonly record struct FakeEntry(byte[] Bytes, DateTimeOffset LastModified, VirtualEntryKind Kind, DateTimeOffset? CreationTime = null);

    /// <summary>
    /// MemoryStream that commits its buffered bytes back to the fake store on Dispose.
    /// For append mode, <paramref name="seed"/> pre-populates the buffer.
    /// </summary>
    private sealed class CommitOnDisposeStream : MemoryStream
    {
        private readonly FakeFileSystemService _store;
        private readonly string _virtualPath;
        private readonly bool _append;
        private bool _disposed;

        public CommitOnDisposeStream(FakeFileSystemService store, string virtualPath, bool append, byte[]? seed = null)
            : base()
        {
            _store = store;
            _virtualPath = virtualPath;
            _append = append;
            if (seed is { Length: > 0 })
            {
                Write(seed, 0, seed.Length);
                // Keep position at end so new writes go after existing content
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;
                var bytes = ToArray();
                _store._entries[_virtualPath] = new FakeEntry(bytes, DateTimeOffset.UtcNow, VirtualEntryKind.File, DateTimeOffset.UtcNow);
            }
            base.Dispose(disposing);
        }
    }
}
