using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Abstractions.Tests.Base;

public class FileToolBasePathTests
{
    [Fact]
    public void ResolveVirtualPath_ShouldReturnVfsResolution_WhenVfsAllows()
    {
        var fs = new FakeFileSystemService().AddMount("/workspace", FileAccessRights.Read);
        using var tool = new ExposedFileTool(fs);

        var result = tool.TestResolveVirtualPath("/workspace/file.txt", FileAccessRights.Read);

        Assert.True(result.IsAllowed);
        Assert.Equal("/workspace/file.txt", result.ResolvedPath);
    }

    [Fact]
    public void ResolveVirtualPath_ShouldReturnDenied_WhenVfsDenies()
    {
        // Mount only /workspace; a path outside it is denied by the VFS.
        var fs = new FakeFileSystemService().AddMount("/workspace", FileAccessRights.Read);
        using var tool = new ExposedFileTool(fs);

        var result = tool.TestResolveVirtualPath("/elsewhere/file.txt", FileAccessRights.Read);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public void ResolveVirtualPath_ShouldApplyValidatorDefenseInDepth_WhenValidatorDenies()
    {
        // VFS allows, but the optional defense-in-depth validator denies the resolved path.
        var fs = new FakeFileSystemService().AddMount("/workspace", FileAccessRights.Read);
        var validator = new StubPathValidator(PathValidationResult.Denied("nope"));
        using var tool = new ExposedFileTool(fs, validator);

        var result = tool.TestResolveVirtualPath("/workspace/file.txt", FileAccessRights.Read);

        Assert.False(result.IsAllowed);
        Assert.Equal("nope", result.DenialReason);
        Assert.Equal("/workspace/file.txt", validator.LastPath);
    }

    [Fact]
    public void ResolveVirtualPath_ShouldAllow_WhenVfsAndValidatorBothAllow()
    {
        var fs = new FakeFileSystemService().AddMount("/workspace", FileAccessRights.Read);
        var validator = new StubPathValidator(PathValidationResult.Allowed("/workspace/file.txt"));
        using var tool = new ExposedFileTool(fs, validator);

        var result = tool.TestResolveVirtualPath("/workspace/file.txt", FileAccessRights.Read);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task EnsureDirectoryExistsAsync_ShouldNotCreate_WhenLastSlashAtRoot()
    {
        var fs = new FakeFileSystemService();
        using var tool = new ExposedFileTool(fs);

        // Path like "/file.txt" -> lastSlash == 0 -> returns without creating.
        await tool.TestEnsureDirectoryExistsAsync("/file.txt");

        Assert.False(await fs.ExistsAsync("/", CancellationToken.None));
    }

    private sealed class StubPathValidator(PathValidationResult result) : IPathValidator
    {
        public string? LastPath { get; private set; }

        public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null)
        {
            LastPath = requestedPath;
            return result;
        }
    }

    private sealed class ExposedFileTool : FileToolBase
    {
        public ExposedFileTool(IFileSystemService fs) : base(fs) { }
        public ExposedFileTool(IFileSystemService fs, IPathValidator validator) : base(fs, validator) { }

        public PathValidationResult TestResolveVirtualPath(string path, FileAccessRights right)
            => ResolveVirtualPath(path, right);

        public Task TestEnsureDirectoryExistsAsync(string path) => EnsureDirectoryExistsAsync(path);

        protected override Task<ToolCallResponse> ExecuteCoreAsync(ToolCallRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ToolCallResponse(true, null, null));
    }
}
