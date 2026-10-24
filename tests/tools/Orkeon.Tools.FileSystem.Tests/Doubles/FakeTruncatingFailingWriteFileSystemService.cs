#pragma warning disable CA2000 // the stream is handed to the caller, who disposes it
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.FileSystem.Tests.Doubles;

/// <summary>
/// Only the first write truncates the target then fails with <see cref="FirstWriteFailure"/> (none: it succeeds);
/// later writes succeed, or fail with <see cref="LaterWriteFailure"/> when set. Records writes, copies and deletes.
/// </summary>
public sealed class FakeTruncatingFailingWriteFileSystemService : IFileSystemService
{
    public FakeFileSystemService Inner { get; } = new();

    /// <summary>Failure of the first write; null lets every write through.</summary>
    public Func<Exception>? FirstWriteFailure { get; set; } = () => new IOException("simulated disk failure");

    /// <summary>Failure of every write after the first; null lets them through.</summary>
    public Func<Exception>? LaterWriteFailure { get; set; }

    public int WriteCalls { get; private set; }
    public List<(string Source, string Destination)> CopyCalls { get; } = [];
    public List<string> DeleteCalls { get; } = [];

    public PathValidationResult ResolveAndValidate(string p, FileAccessRights r) => Inner.ResolveAndValidate(p, r);
    public string? ToVirtualPath(string p) => Inner.ToVirtualPath(p);
    public IReadOnlyList<MountInfo> GetAvailableMounts() => Inner.GetAvailableMounts();

    public async Task<Stream> OpenWriteStreamAsync(string p, CancellationToken ct = default)
    {
        var failure = await NextFailureAsync(p, ct);
        return failure is null ? await Inner.OpenWriteStreamAsync(p, ct) : new ThrowOnWriteStream(failure);
    }

    public async Task<Stream> OpenAppendStreamAsync(string p, CancellationToken ct = default)
    {
        var failure = await NextFailureAsync(p, ct);
        return failure is null ? await Inner.OpenAppendStreamAsync(p, ct) : new ThrowOnWriteStream(failure);
    }

    public async Task<int> WriteAllTextAsync(string p, string c, CancellationToken ct)
    {
        var failure = await NextFailureAsync(p, ct);
        return failure is null ? await Inner.WriteAllTextAsync(p, c, ct) : throw failure;
    }

    public async Task<int> WriteAllBytesAsync(string p, byte[] c, CancellationToken ct)
    {
        var failure = await NextFailureAsync(p, ct);
        return failure is null ? await Inner.WriteAllBytesAsync(p, c, ct) : throw failure;
    }

    public Task CopyAsync(string s, string d, bool overwrite = false, CancellationToken ct = default)
    {
        CopyCalls.Add((s, d));
        return Inner.CopyAsync(s, d, overwrite, ct);
    }

    public Task<bool> DeleteAsync(string p, bool recursive, CancellationToken ct)
    {
        DeleteCalls.Add(p);
        return Inner.DeleteAsync(p, recursive, ct);
    }

    public Task<bool> ExistsAsync(string p, CancellationToken ct) => Inner.ExistsAsync(p, ct);
    public Task CreateDirectoryAsync(string p, CancellationToken ct) => Inner.CreateDirectoryAsync(p, ct);
    public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string r, VirtualEnumerationOptions? o, CancellationToken ct) => Inner.EnumerateFilesAsync(r, o, ct);
    public Task<Stream> OpenReadStreamAsync(string p, CancellationToken ct) => Inner.OpenReadStreamAsync(p, ct);
    public Task<byte[]?> TryReadAllBytesAsync(string p, CancellationToken ct) => Inner.TryReadAllBytesAsync(p, ct);
    public Task<string?> TryReadAllTextAsync(string p, CancellationToken ct) => Inner.TryReadAllTextAsync(p, ct);
    public Task<VirtualEntryKind> GetEntryKindAsync(string p, CancellationToken ct) => Inner.GetEntryKindAsync(p, ct);
    public Task<int> AppendAllTextAsync(string p, string c, CancellationToken ct) => Inner.AppendAllTextAsync(p, c, ct);
    public Task<VirtualFileEntry?> TryGetEntryAsync(string p, CancellationToken ct) => Inner.TryGetEntryAsync(p, ct);

    private async Task<Exception?> NextFailureAsync(string p, CancellationToken ct)
    {
        WriteCalls++;
        if (WriteCalls == 1)
        {
            if (FirstWriteFailure is null) return null;
            await Inner.WriteAllTextAsync(p, string.Empty, ct);
            return FirstWriteFailure();
        }

        return LaterWriteFailure?.Invoke();
    }

    private sealed class ThrowOnWriteStream(Exception failure) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() => throw failure;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw failure;
    }
}
