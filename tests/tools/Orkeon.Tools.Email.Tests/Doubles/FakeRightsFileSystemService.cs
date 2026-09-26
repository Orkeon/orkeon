using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>
/// <see cref="IFileSystemService"/> over the shared <see cref="FakeFileSystemService"/> that, unlike
/// it, enforces each mount's rights — in <see cref="ResolveAndValidate"/>, and on every write with
/// the rights the real service demands (Write and Create for a file, Create for a directory) —
/// and records the right every validation asked for, so a test can tell a read-only mount from a
/// writable one, and one that may not create from one that may.
/// </summary>
internal sealed class FakeRightsFileSystemService : IFileSystemService
{
    private readonly FakeFileSystemService _inner = new();
    private readonly List<(string Path, FileAccessRights Rights)> _mounts = [];

    /// <summary>Every validation asked for, in order.</summary>
    public List<(string Path, FileAccessRights Right)> Validations { get; } = [];

    /// <summary>The shared fake underneath, for seeding and inspecting files.</summary>
    public FakeFileSystemService Inner => _inner;

    /// <summary>Declares a mount with its rights.</summary>
    public FakeRightsFileSystemService AddMount(string virtualPath, FileAccessRights rights)
    {
        _inner.AddMount(virtualPath, rights);
        _mounts.Add((virtualPath, rights));
        return this;
    }

    /// <summary>Seeds a file.</summary>
    public FakeRightsFileSystemService AddFile(string virtualPath, byte[] content)
    {
        _inner.AddFile(virtualPath, content);
        return this;
    }

    /// <inheritdoc />
    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
    {
        Validations.Add((virtualPath, requiredRight));
        var mount = _mounts
            .Where(m => virtualPath == m.Path || virtualPath.StartsWith(m.Path + "/", StringComparison.Ordinal))
            .OrderByDescending(m => m.Path.Length)
            .FirstOrDefault();
        if (mount.Path is null)
            return PathValidationResult.Denied($"No mount for virtual path '{virtualPath}'.");
        return (mount.Rights & requiredRight) == requiredRight
            ? PathValidationResult.Allowed(virtualPath)
            : PathValidationResult.Denied($"The mount '{mount.Path}' does not grant the right this needs.");
    }

    /// <inheritdoc />
    public string? ToVirtualPath(string physicalPath) => _inner.ToVirtualPath(physicalPath);

    /// <inheritdoc />
    public IReadOnlyList<MountInfo> GetAvailableMounts() => _inner.GetAvailableMounts();

    /// <inheritdoc />
    public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct) =>
        _inner.EnumerateFilesAsync(virtualRoot, options, ct);

    /// <inheritdoc />
    public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct) => _inner.OpenReadStreamAsync(virtualPath, ct);

    /// <inheritdoc />
    public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct) => _inner.TryReadAllBytesAsync(virtualPath, ct);

    /// <inheritdoc />
    public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct) => _inner.TryReadAllTextAsync(virtualPath, ct);

    /// <inheritdoc />
    public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct) => _inner.GetEntryKindAsync(virtualPath, ct);

    /// <inheritdoc />
    public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct) => Demand(virtualPath, FileAccessRights.Write | FileAccessRights.Create, () => _inner.WriteAllTextAsync(virtualPath, content, ct));

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct) => _inner.ExistsAsync(virtualPath, ct);

    /// <inheritdoc />
    public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct) => Demand(virtualPath, FileAccessRights.Create, () => _inner.CreateDirectoryAsync(virtualPath, ct));

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct) => _inner.DeleteAsync(virtualPath, recursive, ct);

    /// <inheritdoc />
    public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default) => Demand(virtualPath, FileAccessRights.Write | FileAccessRights.Create, () => _inner.OpenWriteStreamAsync(virtualPath, ct));

    /// <inheritdoc />
    public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default) => _inner.OpenAppendStreamAsync(virtualPath, ct);

    /// <inheritdoc />
    public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default) =>
        _inner.CopyAsync(srcVirtualPath, dstVirtualPath, overwrite, ct);

    /// <inheritdoc />
    public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct) => Demand(virtualPath, FileAccessRights.Write | FileAccessRights.Create, () => _inner.WriteAllBytesAsync(virtualPath, content, ct));

    /// <inheritdoc />
    public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct) => _inner.AppendAllTextAsync(virtualPath, content, ct);

    /// <inheritdoc />
    public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct) => _inner.TryGetEntryAsync(virtualPath, ct);

    /// <summary>Runs <paramref name="write"/> once the mount grants <paramref name="rights"/>, else throws as the real service does.</summary>
    private Task<T> Demand<T>(string virtualPath, FileAccessRights rights, Func<Task<T>> write)
    {
        var check = ResolveAndValidate(virtualPath, rights);
        return check.IsAllowed ? write() : throw new FileAccessDeniedException(check.DenialReason!, virtualPath, rights);
    }

    private Task Demand(string virtualPath, FileAccessRights rights, Func<Task> write)
    {
        var check = ResolveAndValidate(virtualPath, rights);
        return check.IsAllowed ? write() : throw new FileAccessDeniedException(check.DenialReason!, virtualPath, rights);
    }
}
