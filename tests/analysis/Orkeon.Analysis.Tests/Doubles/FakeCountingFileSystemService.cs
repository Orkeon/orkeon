using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Analysis.Tests.Doubles;

/// <summary>
/// Delegating decorator over <see cref="IFileSystemService"/> that records the virtual path
/// of every file read (text, bytes, stream).
/// </summary>
public sealed class FakeCountingFileSystemService : IFileSystemService
{
    private readonly IFileSystemService _inner;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _reads = new();

    public FakeCountingFileSystemService(IFileSystemService inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Path, byte[] Bytes)> _writes = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Source, string Destination)> _copies = new();

    /// <summary>Virtual paths and bytes written so far (text recorded as UTF-8), in call order.</summary>
    public IReadOnlyList<(string Path, byte[] Bytes)> Writes => [.. _writes];

    /// <summary>Copies requested so far, in call order.</summary>
    public IReadOnlyList<(string Source, string Destination)> Copies => [.. _copies];

    /// <summary>Virtual paths read so far, in call order.</summary>
    public IReadOnlyList<string> Reads => [.. _reads];

    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
    {
        _reads.Enqueue(virtualPath);
        return _inner.TryReadAllBytesAsync(virtualPath, ct);
    }

    /// <summary>Virtual paths whose Write right is refused by <see cref="ResolveAndValidate"/>.</summary>
    public HashSet<string> DeniedWritePaths = [];

    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
        => requiredRight.HasFlag(FileAccessRights.Write) && DeniedWritePaths.Contains(virtualPath)
            ? PathValidationResult.Denied($"Write refused on '{virtualPath}'.")
            : _inner.ResolveAndValidate(virtualPath, requiredRight);

    public string? ToVirtualPath(string physicalPath) => _inner.ToVirtualPath(physicalPath);

    public IReadOnlyList<MountInfo> GetAvailableMounts() => _inner.GetAvailableMounts();

    public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
        string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct)
        => _inner.EnumerateFilesAsync(virtualRoot, options, ct);

    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct)
    {
        _reads.Enqueue(virtualPath);
        return _inner.OpenReadStreamAsync(virtualPath, ct);
    }

    public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
    {
        _reads.Enqueue(virtualPath);
        return _inner.TryReadAllTextAsync(virtualPath, ct);
    }

    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct)
        => _inner.GetEntryKindAsync(virtualPath, ct);

    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
    {
        _writes.Enqueue((virtualPath, System.Text.Encoding.UTF8.GetBytes(content)));
        return _inner.WriteAllTextAsync(virtualPath, content, ct);
    }

    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct)
        => _inner.ExistsAsync(virtualPath, ct);

    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct)
        => _inner.CreateDirectoryAsync(virtualPath, ct);

    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct)
        => _inner.DeleteAsync(virtualPath, recursive, ct);

    /// <summary>When set, a byte write first truncates the target through the inner service, then throws this.</summary>
    public Exception? WriteFailure;

    /// <summary>Per-path script of byte writes: each write to the path dequeues one entry; a non-null entry truncates the target then throws it.</summary>
    public Dictionary<string, Queue<Exception?>> WriteOutcomesByPath = [];

    public async Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct)
    {
        if (WriteOutcomesByPath.TryGetValue(virtualPath, out var script) && script.Count > 0 && script.Dequeue() is { } scripted)
        {
            await _inner.WriteAllBytesAsync(virtualPath, [], ct);
            throw scripted;
        }
        if (WriteFailure is not null)
        {
            await _inner.WriteAllBytesAsync(virtualPath, [], ct);
            throw WriteFailure;
        }
        _writes.Enqueue((virtualPath, (byte[])content.Clone()));
        return await _inner.WriteAllBytesAsync(virtualPath, content, ct);
    }

    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct)
        => _inner.AppendAllTextAsync(virtualPath, content, ct);

    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
        => _inner.TryGetEntryAsync(virtualPath, ct);

    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default)
        => _inner.OpenWriteStreamAsync(virtualPath, ct);

    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default)
        => _inner.OpenAppendStreamAsync(virtualPath, ct);

    /// <summary>When set, a copy whose source ends with ".orkeon-edit.bak" (a restore) throws this.</summary>
    public Exception? RestoreCopyFailure;

    public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default)
    {
        _copies.Enqueue((srcVirtualPath, dstVirtualPath));
        if (RestoreCopyFailure is not null && srcVirtualPath.EndsWith(".orkeon-edit.bak", StringComparison.Ordinal))
            throw RestoreCopyFailure;
        return _inner.CopyAsync(srcVirtualPath, dstVirtualPath, overwrite, ct);
    }
}
