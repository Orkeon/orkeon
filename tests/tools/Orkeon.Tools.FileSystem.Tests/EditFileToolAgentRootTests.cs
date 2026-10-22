using System.Text;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

public sealed class EditFileToolAgentRootTests : IDisposable
{
    private const FileAccessRights All = FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create;

    private readonly FakeRightsFileSystemService _fs;
    private readonly EditFileTool _tool;

    public EditFileToolAgentRootTests()
    {
        _fs = new FakeRightsFileSystemService().AddMount("/workspace", All);
        _fs.AddFile("/workspace/job/a.txt", Encoding.UTF8.GetBytes("alpha"));
        _fs.AddFile("/workspace/other/b.txt", Encoding.UTF8.GetBytes("alpha"));
        _tool = new EditFileTool(_fs, new StubPathValidator().AllowAll());
    }

    public void Dispose() => (_tool as IDisposable)?.Dispose();

    private Task<ToolCallResponse> EditAsync(string path) =>
        _tool.CallAsync(new ToolCallRequest("edit_file", new Dictionary<string, object?>
        {
            [ParamPath] = path, ["old_string"] = "alpha", ["new_string"] = "beta",
        }));

    [Fact]
    [Trait("BR", "RT-09a")]
    public async Task ShouldRefuseEdit_WhenPathOutsideAgentRoot()
    {
        await using var scope = AgentRunScope.Begin("agent-1", "writer", "/workspace/job");

        var inside = await EditAsync("/workspace/job/a.txt");
        var outside = await EditAsync("/workspace/other/b.txt");

        Assert.True(inside.Success, inside.Error ?? "Expected success");
        Assert.False(outside.Success);
        Assert.Equal("alpha", await _fs.Inner.TryReadAllTextAsync("/workspace/other/b.txt", TestContext.Current.CancellationToken));
        Assert.DoesNotContain(Path.GetTempPath(), outside.Error);
    }
}
