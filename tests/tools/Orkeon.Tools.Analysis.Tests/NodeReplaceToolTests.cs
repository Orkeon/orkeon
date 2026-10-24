using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.Execution;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Analysis.Tests.Doubles;

namespace Orkeon.Tools.Analysis.Tests;

public sealed class NodeReplaceToolTests : IDisposable
{
    private const string Selector = "Demo.Calc.Sub(int, int)";
    private const string VirtualPath = "/workspace/Calc.cs";
    private const string Original =
        "namespace Demo\n{\n    public class Calc\n    {\n"
        + "        public int Sub(int a, int b)\n        {\n            return a - b;\n        }\n\n"
        + "        public int Add(int a)\n        {\n            return a + 1;\n        }\n    }\n}\n";

    private readonly string _dir = Directory.CreateTempSubdirectory("nodereplace").FullName;
    private readonly TreeSitterParserPool _pool = new();
    private readonly FakeIndexInvalidation _index = new();

    public void Dispose()
    {
        _pool.Dispose();
        Directory.Delete(_dir, true);
    }

    private string Disk(params string[] parts) => System.IO.Path.Combine([_dir, .. parts]);

    private async Task<(DiskBackedFileSystemService Fs, string Hash)> SeedAsync()
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync(VirtualPath, Original, CancellationToken.None);
        return (fs, await HashAsync(fs));
    }

    private async Task<string> HashAsync(IFileSystemService fs)
    {
        using var read = new NodeReadTool(fs, _pool);
        var resp = await read.ExecuteTypedForTest(new NodeReadRequest { Path = VirtualPath, Selector = Selector }, CancellationToken.None);
        return resp.Sha256;
    }

    private static async Task<NodeReplaceResponse> CallAsync(NodeReplaceTool tool, string path, string hash, string text)
    {
        var request = new NodeReplaceRequest { Path = path, Selector = Selector, ExpectedHash = hash, Text = text };
        var method = typeof(NodeReplaceTool).GetMethod(
            "ExecuteTypedAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        try
        {
            return await (Task<NodeReplaceResponse>)method!.Invoke(tool, [request, TestContext.Current.CancellationToken])!;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static async Task<string> RefusalAsync(NodeReplaceTool tool, string path, string hash, string text)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => CallAsync(tool, path, hash, text));
        return ex.Message;
    }

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldChangeOnlyNodeLines_WhenNodeReplacedThroughTool()
    {
        var (fs, hash) = await SeedAsync();
        using var tool = new NodeReplaceTool(fs, _pool, _index);
        const string text = "public int Sub(int a, int b)\n        {\n            var d = a - b;\n            return d;\n        }";

        var result = await CallAsync(tool, VirtualPath, hash, text);

        Assert.Equal(5, result.Range.StartLine);
        Assert.Equal(9, result.Range.EndLine);
        Assert.False(string.IsNullOrEmpty(result.NewHash));
        Assert.NotEqual(hash, result.NewHash);
        var before = Original.Split('\n');
        var after = (await File.ReadAllTextAsync(Disk("Calc.cs"), TestContext.Current.CancellationToken)).Split('\n');
        Assert.Equal(before[..4], after[..4]);
        Assert.Equal(before[8..], after[9..]);
        Assert.Equal("        " + text, string.Join('\n', after[4..9]));
    }

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldMarkIndexDirty_WhenNodeReplaced()
    {
        var (fs, hash) = await SeedAsync();
        using var tool = new NodeReplaceTool(fs, _pool, _index);

        await CallAsync(tool, VirtualPath, hash, "public int Sub(int a, int b)\n        {\n            return 0;\n        }");

        Assert.Equal([VirtualPath], _index.DirtyPaths);
    }

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldLeaveFileByteIdentical_WhenRefused()
    {
        var (fs, hash) = await SeedAsync();
        var bytesBefore = await File.ReadAllBytesAsync(Disk("Calc.cs"), TestContext.Current.CancellationToken);
        using var tool = new NodeReplaceTool(fs, _pool, _index);

        var error = await RefusalAsync(tool, VirtualPath, hash, "public int Sub(int a, int b)\n        {\n            return a - ;;; (\n        ");

        Assert.Contains("parse", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(Disk("Calc.cs"), TestContext.Current.CancellationToken));
        Assert.Empty(_index.DirtyPaths);
    }

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldRefuse_WhenHashIsStale()
    {
        var (fs, staleHash) = await SeedAsync();
        await fs.WriteAllTextAsync(VirtualPath, Original.Replace("a - b", "b - a", StringComparison.Ordinal), CancellationToken.None);
        var actualHash = await HashAsync(fs);
        var bytesBefore = await File.ReadAllBytesAsync(Disk("Calc.cs"), TestContext.Current.CancellationToken);
        using var tool = new NodeReplaceTool(fs, _pool, _index);

        var error = await RefusalAsync(tool, VirtualPath, staleHash, "public int Sub(int a, int b)\n        {\n            return 0;\n        }");

        Assert.Contains(staleHash, error, StringComparison.Ordinal);
        Assert.Contains(actualHash, error, StringComparison.Ordinal);
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(Disk("Calc.cs"), TestContext.Current.CancellationToken));
        Assert.Empty(_index.DirtyPaths);
    }

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldRefuse_WhenMountIsReadOnlyOrOutsideRoot()
    {
        const string text = "public int Sub(int a, int b)\n        {\n            return 0;\n        }";

        var (diskFs, hash) = await SeedAsync();
        await diskFs.WriteAllTextAsync("/workspace/other/Calc.cs", Original, CancellationToken.None);
        var outsideBefore = await File.ReadAllBytesAsync(Disk("other", "Calc.cs"), TestContext.Current.CancellationToken);
        await using (AgentRunScope.Begin(null, null, "/workspace/sub"))
        {
            using var rooted = new NodeReplaceTool(diskFs, _pool, _index);
            var outside = await RefusalAsync(rooted, "/workspace/other/Calc.cs", hash, text);
            Assert.Contains("outside the agent working root", outside, StringComparison.Ordinal);
        }
        Assert.Equal(outsideBefore, await File.ReadAllBytesAsync(Disk("other", "Calc.cs"), TestContext.Current.CancellationToken));

        var bytes = System.Text.Encoding.UTF8.GetBytes(Original);
        var roFs = new FakeRightsFileSystemService().AddMount("/workspace", FileAccessRights.Read).AddFile(VirtualPath, bytes);
        using var roTool = new NodeReplaceTool(roFs, _pool, _index);
        var readOnly = await RefusalAsync(roTool, VirtualPath, hash, text);
        Assert.Contains("does not grant the right", readOnly, StringComparison.Ordinal);
        Assert.Equal(bytes, await roFs.TryReadAllBytesAsync(VirtualPath, TestContext.Current.CancellationToken));
        Assert.Empty(_index.DirtyPaths);
    }

    [Fact]
    [Trait("BR", "RT-39")]
    public async Task ShouldMarkCanonicalVirtualPathDirty_WhenPathIsNotCanonical()
    {
        var (fs, hash) = await SeedAsync();
        using var tool = new NodeReplaceTool(fs, _pool, _index);

        await CallAsync(tool, "/workspace/./Calc.cs", hash, "public int Sub(int a, int b)\n        {\n            return 0;\n        }");

        Assert.Equal([VirtualPath], _index.DirtyPaths);
    }
}
