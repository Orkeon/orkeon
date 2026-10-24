using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Tools.FileSystem.Tests.Doubles;

/// <summary>
/// Delegating decorator over <see cref="IFileSystemService"/> that records the virtual path
/// of every file read (text, bytes, stream) and the bytes pulled through returned streams.
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

    /// <summary>Bytes read through streams opened by <see cref="OpenReadStreamAsync"/>.</summary>
    public long BytesRead => System.Threading.Interlocked.Read(ref _bytesRead);

    /// <summary>Whole-file text or byte reads requested.</summary>
    public int WholeFileReads { get; private set; }

    private long _bytesRead;

    internal void AddBytes(int n) => System.Threading.Interlocked.Add(ref _bytesRead, n);

    /// <summary>Virtual paths read so far, in call order.</summary>
    public IReadOnlyList<string> Reads => [.. _reads];

    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
    {
        _reads.Enqueue(virtualPath);
        WholeFileReads++;
        return _inner.TryReadAllBytesAsync(virtualPath, ct);
    }

    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
        => _inner.ResolveAndValidate(virtualPath, requiredRight);

    public string? ToVirtualPath(string physicalPath) => _inner.ToVirtualPath(physicalPath);

    public IReadOnlyList<MountInfo> GetAvailableMounts() => _inner.GetAvailableMounts();

    public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(
        string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct)
        => _inner.EnumerateFilesAsync(virtualRoot, options, ct);

    public async Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct)
    {
        _reads.Enqueue(virtualPath);
        var inner = await _inner.OpenReadStreamAsync(virtualPath, ct).ConfigureAwait(false);
        return new CountingStream(inner, this);
    }

    private sealed class CountingStream(Stream inner, FakeCountingFileSystemService owner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            owner.AddBytes(n);
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            owner.AddBytes(n);
            return n;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var n = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            owner.AddBytes(n);
            return n;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
    {
        _reads.Enqueue(virtualPath);
        WholeFileReads++;
        return _inner.TryReadAllTextAsync(virtualPath, ct);
    }

    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct)
        => _inner.GetEntryKindAsync(virtualPath, ct);

    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct)
        => _inner.WriteAllTextAsync(virtualPath, content, ct);

    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct)
        => _inner.ExistsAsync(virtualPath, ct);

    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct)
        => _inner.CreateDirectoryAsync(virtualPath, ct);

    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct)
        => _inner.DeleteAsync(virtualPath, recursive, ct);

    /// <summary>When set, a byte write waits on it (after raising <see cref="WriteReached"/>) until the test releases it.</summary>
    public TaskCompletionSource? BlockWrites { get => _blockWrites; set => _blockWrites = value; }

    private TaskCompletionSource? _blockWrites;

    /// <summary>Raised when a byte write is reached while <see cref="BlockWrites"/> is set.</summary>
    public TaskCompletionSource WriteReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct)
    {
        var gate = System.Threading.Interlocked.Exchange(ref _blockWrites, null);
        if (gate is not null)
        {
            WriteReached.TrySetResult();
            await gate.Task.ConfigureAwait(false);
        }
        return await _inner.WriteAllBytesAsync(virtualPath, content, ct).ConfigureAwait(false);
    }

    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct)
        => _inner.AppendAllTextAsync(virtualPath, content, ct);

    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
        => _inner.TryGetEntryAsync(virtualPath, ct);

    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default)
        => _inner.OpenWriteStreamAsync(virtualPath, ct);

    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default)
        => _inner.OpenAppendStreamAsync(virtualPath, ct);

    public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default)
        => _inner.CopyAsync(srcVirtualPath, dstVirtualPath, overwrite, ct);
}
