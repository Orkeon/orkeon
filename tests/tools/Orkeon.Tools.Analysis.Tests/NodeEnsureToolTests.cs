using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Analysis.Tests.Doubles;

namespace Orkeon.Tools.Analysis.Tests;

public sealed class NodeEnsureToolTests : IDisposable
{
    private const string Original = "public class A\n{\n    public int X() { return 1; }\n}\n";

    private readonly string _dir = Directory.CreateTempSubdirectory("nodeensure").FullName;
    private readonly TreeSitterParserPool _pool = new();

    public void Dispose()
    {
        _pool.Dispose();
        Directory.Delete(_dir, true);
    }

    [Fact]
    [Trait("BR", "RT-42")]
    public async Task ShouldAddUsingOnce_WhenNodeEnsureCalledTwice()
    {
        var disk = new DiskBackedFileSystemService(_dir, "/workspace");
        await disk.WriteAllTextAsync("/workspace/A.cs", Original, CancellationToken.None);
        var fs = new FakeCountingFileSystemService(disk);
        using var tool = new NodeEnsureTool(fs, _pool);
        var request = new NodeEnsureRequest { Op = "ensure_using", Path = "/workspace/A.cs", Name = "System.Linq" };
        var physical = System.IO.Path.Combine(_dir, "A.cs");

        var first = await tool.ExecuteTypedForTest<NodeEnsureRequest, NodeEnsureResponse>(request, TestContext.Current.CancellationToken);
        var afterFirst = await File.ReadAllTextAsync(physical, TestContext.Current.CancellationToken);
        var writes = fs.Writes.Count;
        var second = await tool.ExecuteTypedForTest<NodeEnsureRequest, NodeEnsureResponse>(request, TestContext.Current.CancellationToken);

        Assert.True(first.Applied);
        Assert.Equal(1, afterFirst.Split("using System.Linq;").Length - 1);
        Assert.False(second.Applied);
        Assert.Equal(afterFirst, await File.ReadAllTextAsync(physical, TestContext.Current.CancellationToken));
        Assert.Equal(writes, fs.Writes.Count);
    }
}
