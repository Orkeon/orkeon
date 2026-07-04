using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Abstractions.Base;

namespace Orkeon.Tools.Abstractions.Tests.Base;

public class FileToolBaseTests
{
    [Fact]
    public async Task EnsureDirectoryExistsAsync_ShouldCreateDirectory_WhenItDoesNotExist()
    {
        var fs = new FakeFileSystemService();
        using var tool = new TestFileToolBase(fs);

        await tool.TestEnsureDirectoryExistsAsync("/workspace/output/report.txt", TestContext.Current.CancellationToken);

        Assert.True(await fs.ExistsAsync("/workspace/output", CancellationToken.None));
    }

    [Fact]
    public async Task EnsureDirectoryExistsAsync_ShouldBeIdempotent_WhenDirectoryAlreadyExists()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory("/workspace/output");
        using var tool = new TestFileToolBase(fs);

        // Should not throw when called twice
        await tool.TestEnsureDirectoryExistsAsync("/workspace/output/file.txt", TestContext.Current.CancellationToken);
        await tool.TestEnsureDirectoryExistsAsync("/workspace/output/file.txt", TestContext.Current.CancellationToken);

        Assert.True(await fs.ExistsAsync("/workspace/output", CancellationToken.None));
    }

    [Fact]
    public async Task EnsureDirectoryExistsAsync_ShouldDoNothing_WhenPathHasNoDirectory()
    {
        var fs = new FakeFileSystemService();
        using var tool = new TestFileToolBase(fs);

        // Should not throw for top-level file with no directory component
        var exception = await Record.ExceptionAsync(
            () => tool.TestEnsureDirectoryExistsAsync("file.txt", TestContext.Current.CancellationToken));
        Assert.Null(exception);
    }

}

internal class TestFileToolBase : FileToolBase
{
    public TestFileToolBase(IFileSystemService fs) : base(fs) { }

    public Task TestEnsureDirectoryExistsAsync(string virtualFilePath, CancellationToken ct = default)
        => EnsureDirectoryExistsAsync(virtualFilePath, ct);

    protected override Task<ToolCallResponse> ExecuteCoreAsync(ToolCallRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new ToolCallResponse(true, null, null));
}
