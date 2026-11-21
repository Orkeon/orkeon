using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// <see cref="IFileSystemService"/> over a <see cref="FakeFileSystemService"/> that counts what is read: the
/// whole-file reads (<see cref="WholeReads"/>) and the bytes taken from the read streams (<see cref="BytesRead"/>).
/// </summary>
public sealed class FakeCountingReadFileSystemService(FakeFileSystemService inner) : IFileSystemService
{
    /// <summary>How many whole-file reads (text or bytes) were made.</summary>
    public int WholeReads { get; private set; }

    /// <summary>How many bytes the read streams handed out.</summary>
    public long BytesRead => _bytesRead;

    private long _bytesRead;

    public async Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct) =>
        new CountingStream(await inner.OpenReadStreamAsync(virtualPath, ct).ConfigureAwait(false), n => Interlocked.Add(ref _bytesRead, n));

    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct)
    {
        WholeReads++;
        return inner.TryReadAllBytesAsync(virtualPath, ct);
    }

    public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct)
    {
        WholeReads++;
        return inner.TryReadAllTextAsync(virtualPath, ct);
    }

    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct) => inner.ExistsAsync(virtualPath, ct);
    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct) => inner.TryGetEntryAsync(virtualPath, ct);
    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight) => inner.ResolveAndValidate(virtualPath, requiredRight);
    public string? ToVirtualPath(string physicalPath) => inner.ToVirtualPath(physicalPath);
    public IReadOnlyList<MountInfo> GetAvailableMounts() => inner.GetAvailableMounts();
    public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct) => inner.EnumerateFilesAsync(virtualRoot, options, ct);
    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct) => inner.GetEntryKindAsync(virtualPath, ct);
    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct) => inner.WriteAllTextAsync(virtualPath, content, ct);
    public System.Threading.Tasks.Task CreateDirectoryAsync(string virtualPath, CancellationToken ct) => inner.CreateDirectoryAsync(virtualPath, ct);
    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct) => inner.DeleteAsync(virtualPath, recursive, ct);
    public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct) => inner.WriteAllBytesAsync(virtualPath, content, ct);
    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct) => inner.AppendAllTextAsync(virtualPath, content, ct);
    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default) => inner.OpenWriteStreamAsync(virtualPath, ct);
    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default) => inner.OpenAppendStreamAsync(virtualPath, ct);
    public System.Threading.Tasks.Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default) => inner.CopyAsync(srcVirtualPath, dstVirtualPath, overwrite, ct);

    // A read-only stream that reports each read's byte count.
    private sealed class CountingStream(Stream inner, Action<int> counted) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }

        private int Count(int read)
        {
            counted(read);
            return read;
        }
    }
}
