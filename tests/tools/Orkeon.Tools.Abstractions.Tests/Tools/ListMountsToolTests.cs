using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
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
        var root = ResultJson(response);
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(0, root.GetProperty("mounts").GetArrayLength());
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
        var dtos = ResultJson(response).GetProperty("mounts");
        Assert.Equal(3, dtos.GetArrayLength());

        var workspace = dtos[0];
        Assert.Equal("/workspace", workspace.GetProperty("virtual_path").GetString());
        Assert.Equal("rw", workspace.GetProperty("access").GetString());
        var overrides = workspace.GetProperty("overrides");
        Assert.Equal(1, overrides.GetArrayLength());
        Assert.Equal("secrets", overrides[0].GetProperty("virtual_path").GetString());
        Assert.Equal("ro", overrides[0].GetProperty("access").GetString());

        Assert.Equal("rwnd", dtos[1].GetProperty("access").GetString());
        Assert.Equal("ro", dtos[2].GetProperty("access").GetString());
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

        var dtos = ResultJson(response).GetProperty("mounts");
        Assert.Equal(FileAccessRights.Write.ToString(), dtos[0].GetProperty("access").GetString());
    }

    /// <summary>
    /// VFS-90 D-08: a mount's id is for hosts and Studio, never for agents. The DTO has no id
    /// field, so <c>list_mounts</c> cannot leak one whatever the registry carries.
    /// </summary>
    [Fact]
    public async Task CallAsync_NeverExposesTheMountId()
    {
        var mounts = new List<MountInfo>
        {
            new("/output", FileAccessRights.ReadWrite, []) { Id = Orkeon.Domain.Common.MountId.Create() },
        };
        using var tool = new ListMountsTool(new StubFileSystemService(mounts));

        var response = await tool.CallAsync(new ProtocolToolCallRequest("list_mounts", []), TestContext.Current.CancellationToken);

        var mountsJson = ResultJson(response).GetProperty("mounts");
        Assert.Equal(1, mountsJson.GetArrayLength());
        Assert.Equal("rw", mountsJson[0].GetProperty("access").GetString());
        Assert.DoesNotContain(typeof(MountInfoDto).GetProperties(), p => p.Name.Contains("Id", StringComparison.Ordinal));
        Assert.DoesNotContain("|", mountsJson.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-27")]
    public async Task ShouldExposeVirtualPathAndAccess_WhenListMountsCalled()
    {
        var mounts = new List<MountInfo>
        {
            new("/workspace", FileAccessRights.ReadWrite,
                [new SubPathOverride("secrets", FileAccessRights.ReadOnly)]),
        };
        using var tool = new ListMountsTool(new StubFileSystemService(mounts));

        var response = await tool.CallAsync(new ProtocolToolCallRequest("list_mounts", []), TestContext.Current.CancellationToken);

        Assert.IsType<ToolBase<ListMountsRequest, ListMountsResponse>>(tool, exactMatch: false);
        var mount = ResultJson(response).GetProperty("mounts")[0];
        Assert.Equal(
            "{\"virtual_path\":\"/workspace\",\"access\":\"rw\",\"overrides\":[{\"virtual_path\":\"secrets\",\"access\":\"ro\"}]}",
            mount.GetRawText());
    }

    private static System.Text.Json.JsonElement ResultJson(Orkeon.Domain.Tools.Protocol.ToolCallResponse response)
    {
        Assert.True(response.Success);
        return System.Text.Json.JsonSerializer.SerializeToElement(response.Result);
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
