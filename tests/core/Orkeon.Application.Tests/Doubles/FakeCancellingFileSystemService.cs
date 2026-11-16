using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// <see cref="IFileSystemService"/> over a <see cref="FakeFileSystemService"/> that cancels the run on the
/// first probe of an entry (<see cref="IFileSystemService.ExistsAsync"/>,
/// <see cref="IFileSystemService.TryGetEntryAsync"/>) once <see cref="Armed"/>: the cancellation lands
/// exactly while the end-of-task deliverable probe runs.
/// </summary>
public sealed class FakeCancellingFileSystemService(FakeFileSystemService inner, CancellationTokenSource run) : IFileSystemService
{
    /// <summary>Set to make the next probe cancel <c>run</c> and throw.</summary>
    public bool Armed { get; set; }

    /// <summary>How many probes cancelled the run.</summary>
    public int Cancellations { get; private set; }

    private void CancelWhenArmed()
    {
        if (!Armed)
            return;
        Armed = false;
        Cancellations++;
        run.Cancel();
        run.Token.ThrowIfCancellationRequested();
    }

    public async Task<bool> ExistsAsync(string virtualPath, CancellationToken ct)
    {
        CancelWhenArmed();
        return await inner.ExistsAsync(virtualPath, ct).ConfigureAwait(false);
    }

    public async Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct)
    {
        CancelWhenArmed();
        return await inner.TryGetEntryAsync(virtualPath, ct).ConfigureAwait(false);
    }

    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight) => inner.ResolveAndValidate(virtualPath, requiredRight);
    public string? ToVirtualPath(string physicalPath) => inner.ToVirtualPath(physicalPath);
    public IReadOnlyList<MountInfo> GetAvailableMounts() => inner.GetAvailableMounts();
    public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct) => inner.EnumerateFilesAsync(virtualRoot, options, ct);
    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct) => inner.OpenReadStreamAsync(virtualPath, ct);
    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct) => inner.TryReadAllBytesAsync(virtualPath, ct);
    public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct) => inner.TryReadAllTextAsync(virtualPath, ct);
    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct) => inner.GetEntryKindAsync(virtualPath, ct);
    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct) => inner.WriteAllTextAsync(virtualPath, content, ct);
    public System.Threading.Tasks.Task CreateDirectoryAsync(string virtualPath, CancellationToken ct) => inner.CreateDirectoryAsync(virtualPath, ct);
    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct) => inner.DeleteAsync(virtualPath, recursive, ct);
    public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct) => inner.WriteAllBytesAsync(virtualPath, content, ct);
    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct) => inner.AppendAllTextAsync(virtualPath, content, ct);
    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default) => inner.OpenWriteStreamAsync(virtualPath, ct);
    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default) => inner.OpenAppendStreamAsync(virtualPath, ct);
    public System.Threading.Tasks.Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default) => inner.CopyAsync(srcVirtualPath, dstVirtualPath, overwrite, ct);
}
