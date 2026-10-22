using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

public sealed class FileWriteToolAgentRootTests : IDisposable
{
    private const FileAccessRights All = FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create;

    private readonly FakeRightsFileSystemService _fs;
    private readonly FileWriteTool _tool;

    public FileWriteToolAgentRootTests()
    {
        _fs = new FakeRightsFileSystemService()
            .AddMount("/workspace", All)
            .AddMount("/output", All);
        _tool = new FileWriteTool(_fs, new StubPathValidator().AllowAll());
    }

    public void Dispose() => (_tool as IDisposable)?.Dispose();

    private Task<ToolCallResponse> WriteAsync(string path) =>
        _tool.CallAsync(new ToolCallRequest(
            ToolName: "file_write",
            Parameters: new Dictionary<string, object?> { [ParamPath] = path, ["content"] = "data" }));

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldWriteInsideRoot_WhenAgentRootDeclared()
    {
        await using var scope = AgentRunScope.Begin("agent-1", "writer", "/workspace/job");

        Assert.Equal("/workspace/job", AgentRunScope.Current?.Root);
        var response = await WriteAsync("/workspace/job/a.txt");

        Assert.True(response.Success, response.Error ?? "Expected success");
        Assert.Equal("data", await _fs.Inner.TryReadAllTextAsync("/workspace/job/a.txt", TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldRefuseFileWrite_WhenPathOutsideAgentRoot()
    {
        await using var scope = AgentRunScope.Begin("agent-1", "writer", "/workspace/job");

        var response = await WriteAsync("/workspace/other/b.txt");

        Assert.False(response.Success);
        Assert.Null(await _fs.Inner.TryReadAllTextAsync("/workspace/other/b.txt", TestContext.Current.CancellationToken));
        Assert.DoesNotContain(Path.GetTempPath(), response.Error);
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldAllowWriteOnOtherMount_WhenPathIsOnOutputMount()
    {
        await using var scope = AgentRunScope.Begin("agent-1", "writer", "/workspace/job");

        Assert.Equal("/workspace/job", AgentRunScope.Current?.Root);
        var response = await WriteAsync("/output/log.txt");

        Assert.True(response.Success, response.Error ?? "Expected success");
        Assert.Equal("data", await _fs.Inner.TryReadAllTextAsync("/output/log.txt", TestContext.Current.CancellationToken));
    }
}
