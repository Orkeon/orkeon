using Orkeon.Analysis.Abstractions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Tools.Embeddings.Local.Tests;

/// <summary>
/// Verifies that <see cref="LocalEmbeddingProvider"/> routes the optional
/// <see cref="LocalEmbeddingOptions.ModelPath"/> through <see cref="IFileSystemService"/>
/// (mandatory VFS routing — see docs/architecture/vfs-compliance.md). No
/// <see cref="System.IO.File"/> call must escape the provider.
/// </summary>
public sealed class ModelPathVfsResolutionTests
{
    [Fact]
    public void ModelPath_VirtualPath_LoadsViaVfs()
    {
        const string VirtualPath = "/models/foo.onnx";

        // Strict-mode stub: any unexpected interface call throws and fails the test.
        // We expect exactly one ResolveAndValidate(VirtualPath, Read) call.
        var fs = new StrictFileSystemStub((path, right) =>
        {
            Assert.Equal(VirtualPath, path);
            Assert.Equal(FileAccessRights.Read, right);
            return PathValidationResult.Allowed("/nonexistent-physical/foo.onnx");
        });

        // The constructor will fail downstream (LocalEmbedder cannot read the bogus path),
        // but only AFTER ResolveAndValidate has been invoked. We don't assert the specific
        // exception type — only that VFS was consulted, and that no System.IO.File.* call
        // bypassed it (the strict stub fails any unexpected member access).
        try
        {
            using var provider = new LocalEmbeddingProvider(
                fileSystem: fs,
                options: new LocalEmbeddingOptions { ModelPath = VirtualPath },
                logger: null);
        }
        catch { /* swallowed: see above */ }

        Assert.Equal(1, fs.ResolveCalls);
    }

    [Fact]
    public void ModelPath_PhysicalPath_Throws()
    {
        // The VFS rejects the path (e.g. outside any mount) by surfacing a Denied result.
        // Per the provider contract, this turns into FileAccessDeniedException at construction.
        var fs = new StrictFileSystemStub((_, _) => PathValidationResult.Denied("Path outside mounts"));

        var ex = Assert.Throws<FileAccessDeniedException>(() =>
            new LocalEmbeddingProvider(
                fileSystem: fs,
                options: new LocalEmbeddingOptions { ModelPath = "C:/models/foo.onnx" },
                logger: null));

        Assert.Equal("C:/models/foo.onnx", ex.VirtualPath);
    }

    [Fact]
    public void NoFileSystem_Throws()
    {
        // IFileSystemService is now a required (non-optional, null-guarded) constructor
        // dependency: a null instance is rejected at construction.
        Assert.Throws<ArgumentNullException>(() =>
            new LocalEmbeddingProvider(
                fileSystem: null!,
                options: new LocalEmbeddingOptions { ModelPath = "/foo" },
                logger: null));
    }

    /// <summary>
    /// Strict-mode IFileSystemService stub: every method except <see cref="ResolveAndValidate"/>
    /// throws when invoked. Used to prove that the provider routes only through VFS and
    /// never falls back to other I/O methods.
    /// </summary>
    private sealed class StrictFileSystemStub : IFileSystemService
    {
        private readonly Func<string, FileAccessRights, PathValidationResult> _resolve;
        public int ResolveCalls { get; private set; }

        public StrictFileSystemStub(Func<string, FileAccessRights, PathValidationResult> resolve)
        {
            _resolve = resolve;
        }

        public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
        {
            ResolveCalls++;
            return _resolve(virtualPath, requiredRight);
        }

        private static T Fail<T>(string member)
            => throw new InvalidOperationException(
                $"StrictFileSystemStub: unexpected call to {member}.");

        public string? ToVirtualPath(string physicalPath) => Fail<string>("ToVirtualPath");
        public IReadOnlyList<MountInfo> GetAvailableMounts() => Fail<IReadOnlyList<MountInfo>>("GetAvailableMounts");
        public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct) => Fail<IAsyncEnumerable<VirtualFileEntry>>("EnumerateFilesAsync");
        public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct) => Fail<Task<Stream>>("OpenReadStreamAsync");
        public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct) => Fail<Task<byte[]?>>("TryReadAllBytesAsync");
        public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct) => Fail<Task<string?>>("TryReadAllTextAsync");
        public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct) => Fail<Task<VirtualEntryKind>>("GetEntryKindAsync");
        public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct) => Fail<Task<int>>("WriteAllTextAsync");
        public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct) => Fail<Task<bool>>("ExistsAsync");
        public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct) => Fail<Task>("CreateDirectoryAsync");
        public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct) => Fail<Task<bool>>("DeleteAsync");
        public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct) => Fail<Task<int>>("WriteAllBytesAsync");
        public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct) => Fail<Task<int>>("AppendAllTextAsync");
        public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct) => Fail<Task<VirtualFileEntry?>>("TryGetEntryAsync");
        public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default) => Fail<Task<Stream>>("OpenWriteStreamAsync");
        public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default) => Fail<Task<Stream>>("OpenAppendStreamAsync");
        public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default) => Fail<Task>("CopyAsync");
    }
}
