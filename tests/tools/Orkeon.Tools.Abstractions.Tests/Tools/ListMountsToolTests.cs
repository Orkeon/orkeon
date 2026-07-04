using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Tools;
using ProtocolToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;

namespace Orkeon.Tools.Abstractions.Tests.Tools;

public class ListMountsToolTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenFileSystemServiceIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ListMountsTool(null!));
    }

    [Fact]
    public void Properties_ShouldExposeExpectedMetadata()
    {
        using var tool = new ListMountsTool(new StubFileSystemService([]));

        Assert.Equal("list_mounts", tool.Name);
        Assert.Equal("File Operations", tool.Category);
        Assert.Contains("mounts", tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CallAsync_ShouldReturnEmptyMounts_WhenNoMountsConfigured()
    {
        using var tool = new ListMountsTool(new StubFileSystemService([]));

        var response = await tool.CallAsync(new ProtocolToolCallRequest("list_mounts", []), TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        var dict = Assert.IsType<Dictionary<string, object>>(response.Result);
        Assert.Equal(true, dict["success"]);
        var mounts = Assert.IsType<List<MountInfoDto>>(dict["mounts"]);
        Assert.Empty(mounts);
    }

    [Fact]
    public async Task CallAsync_ShouldMapMountsWithRightsAndOverrides_WhenMountsConfigured()
    {
        var mounts = new List<MountInfo>
        {
            new("/workspace", FileAccessRights.ReadWrite,
                [new SubPathOverride("secrets", FileAccessRights.ReadOnly)]),
            new("/output", FileAccessRights.ReadWriteNoDelete, []),
            new("/tmp", FileAccessRights.ReadOnly, []),
        };
        using var tool = new ListMountsTool(new StubFileSystemService(mounts));

        var response = await tool.CallAsync(new ProtocolToolCallRequest("list_mounts", []), TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        var dict = Assert.IsType<Dictionary<string, object>>(response.Result);
        var dtos = Assert.IsType<List<MountInfoDto>>(dict["mounts"]);
        Assert.Equal(3, dtos.Count);

        var workspace = dtos[0];
        Assert.Equal("/workspace", workspace.Path);
        Assert.Equal("rw", workspace.Rights);
        var ov = Assert.Single(workspace.Overrides);
        Assert.Equal("secrets", ov.Path);
        Assert.Equal("ro", ov.Rights);

        Assert.Equal("rwnd", dtos[1].Rights);
        Assert.Equal("ro", dtos[2].Rights);
    }

    [Fact]
    public async Task CallAsync_ShouldFormatUnknownRights_UsingEnumToString()
    {
        var mounts = new List<MountInfo>
        {
            new("/special", FileAccessRights.Write, []),
        };
        using var tool = new ListMountsTool(new StubFileSystemService(mounts));

        var response = await tool.CallAsync(new ProtocolToolCallRequest("list_mounts", []), TestContext.Current.CancellationToken);

        var dict = Assert.IsType<Dictionary<string, object>>(response.Result);
        var dtos = Assert.IsType<List<MountInfoDto>>(dict["mounts"]);
        Assert.Equal(FileAccessRights.Write.ToString(), dtos[0].Rights);
    }

    /// <summary>
    /// Minimal IFileSystemService stub that only supports GetAvailableMounts,
    /// which is the single method exercised by ListMountsTool.
    /// </summary>
    private sealed class StubFileSystemService(IReadOnlyList<MountInfo> mounts) : IFileSystemService
    {
        public IReadOnlyList<MountInfo> GetAvailableMounts() => mounts;

        public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
            => throw new NotSupportedException();
        public string? ToVirtualPath(string physicalPath) => throw new NotSupportedException();
        public IAsyncEnumerable<VirtualFileEntry> EnumerateFilesAsync(string virtualRoot, VirtualEnumerationOptions? options, CancellationToken ct)
            => throw new NotSupportedException();
        public Task<Stream> OpenReadStreamAsync(string virtualPath, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]?> TryReadAllBytesAsync(string virtualPath, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> TryReadAllTextAsync(string virtualPath, CancellationToken ct) => throw new NotSupportedException();
        public Task<VirtualEntryKind> GetEntryKindAsync(string virtualPath, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> WriteAllTextAsync(string virtualPath, string content, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string virtualPath, CancellationToken ct) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(string virtualPath, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string virtualPath, bool recursive, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenWriteStreamAsync(string virtualPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Stream> OpenAppendStreamAsync(string virtualPath, CancellationToken ct = default) => throw new NotSupportedException();
        public Task CopyAsync(string srcVirtualPath, string dstVirtualPath, bool overwrite = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> WriteAllBytesAsync(string virtualPath, byte[] content, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> AppendAllTextAsync(string virtualPath, string content, CancellationToken ct) => throw new NotSupportedException();
        public Task<VirtualFileEntry?> TryGetEntryAsync(string virtualPath, CancellationToken ct) => throw new NotSupportedException();
    }
}
