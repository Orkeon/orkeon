using System.Text;
using Orkeon.Analysis.Abstractions.Editing;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tests.Shared.Timing;
using Orkeon.Tools.FileSystem.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Tools.FileSystem.Tests;

[Collection("EditFileToolSharedPaths")]
public sealed class EditFileToolLockTests : IDisposable
{
    private const FileAccessRights All = FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create;

    private readonly FakeRightsFileSystemService _fs;
    private readonly FakeCountingFileSystemService _counting;
    private readonly EditFileTool _tool;

    public EditFileToolLockTests()
    {
        _fs = new FakeRightsFileSystemService().AddMount("/workspace", All);
        _counting = new FakeCountingFileSystemService(_fs);
        _tool = new EditFileTool(_counting, new StubPathValidator().AllowAll());
    }

    public void Dispose() => (_tool as IDisposable)?.Dispose();

    private static string UniquePath() => $"/workspace/lock-{Guid.NewGuid():N}.txt";

    private Task<ToolCallResponse> EditAsync(string path, string oldString, string newString) =>
        _tool.CallAsync(new ToolCallRequest("edit_file", new Dictionary<string, object?>
        {
            [ParamPath] = path,
            ["old_string"] = oldString,
            ["new_string"] = newString,
        }));

    private async Task<string?> ReadAsync(string path) =>
        await _fs.Inner.TryReadAllTextAsync(path, TestContext.Current.CancellationToken);

    [Fact]
    [Trait("BR", "R3")]
    public async Task ShouldRefuseEdit_WhenFileIsHeldByAnEditBatch()
    {
        var path = UniquePath();
        _fs.AddFile(path, Encoding.UTF8.GetBytes("alpha"));
        Assert.True(FileEditLocks.TryAcquire([path], "batch-1", out _));
        try
        {
            var response = await EditAsync(path, "alpha", "beta");

            Assert.False(response.Success);
            Assert.Contains("is held by another edit in progress.", response.Error);
            Assert.Equal("alpha", await ReadAsync(path));
        }
        finally
        {
            FileEditLocks.Release([path], "batch-1");
        }
    }

    [Fact]
    [Trait("BR", "R3")]
    public async Task ShouldRefuseSecondEdit_WhenTwoEditFileRunOnOneFile()
    {
        var path = UniquePath();
        _fs.AddFile(path, Encoding.UTF8.GetBytes("alpha"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _counting.BlockWrites = gate;

        var first = EditAsync(path, "alpha", "beta");
        await _counting.WriteReached.Task.WaitAsync(Polling.DefaultTimeout, TestContext.Current.CancellationToken);

        var second = await EditAsync(path, "alpha", "gamma");
        gate.SetResult();
        var firstResponse = await first;

        Assert.False(second.Success);
        Assert.Contains("is held by another edit in progress.", second.Error);
        Assert.True(firstResponse.Success, firstResponse.Error ?? "Expected success");
        Assert.Equal("beta", await ReadAsync(path));
    }

    [Fact]
    [Trait("BR", "R3")]
    public async Task ShouldReleaseLock_WhenEditFails()
    {
        var path = UniquePath();
        _fs.AddFile(path, Encoding.UTF8.GetBytes("alpha"));

        var response = await EditAsync(path, "zeta", "y");

        Assert.False(response.Success);
        Assert.Contains("not found", response.Error, StringComparison.OrdinalIgnoreCase);
        var acquired = FileEditLocks.TryAcquire([path], "probe", out var held);
        FileEditLocks.Release([path], "probe");
        Assert.True(acquired, held);
    }
}
