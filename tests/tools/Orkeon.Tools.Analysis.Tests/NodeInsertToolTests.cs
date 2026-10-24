using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.FileSystem;
using Orkeon.Tools.Analysis.Tests.Doubles;

namespace Orkeon.Tools.Analysis.Tests;

public sealed class NodeInsertToolTests : IDisposable
{
    private const string VirtualPath = "/workspace/Calc.cs";
    private const string Original =
        "namespace Demo\n{\n    public class Calc\n    {\n"
        + "        public int Sub(int a, int b)\n        {\n            return a - b;\n        }\n\n"
        + "        public int Add(int a)\n        {\n            return a + 1;\n        }\n    }\n}\n";

    private readonly string _dir = Directory.CreateTempSubdirectory("nodeinsert").FullName;
    private readonly TreeSitterParserPool _pool = new();
    private readonly FakeIndexInvalidation _index = new();

    public void Dispose()
    {
        _pool.Dispose();
        Directory.Delete(_dir, true);
    }

    private async Task<DiskBackedFileSystemService> SeedAsync()
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync(VirtualPath, Original, CancellationToken.None);
        return fs;
    }

    private static async Task<NodeInsertResponse> CallAsync(NodeInsertTool tool, string selector, string position, string text)
    {
        var request = new NodeInsertRequest { Path = VirtualPath, Selector = selector, Position = position, Text = text };
        var method = typeof(NodeInsertTool).GetMethod(
            "ExecuteTypedAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        try
        {
            return await (Task<NodeInsertResponse>)method!.Invoke(tool, [request, TestContext.Current.CancellationToken])!;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldInsertThroughTool_WhenPositionIsInside()
    {
        var fs = await SeedAsync();
        using var tool = new NodeInsertTool(fs, _pool, _index);

        var result = await CallAsync(tool, "Demo.Calc", "inside", "public int Mul(int a)\n{\n    return a * 2;\n}");

        var text = await File.ReadAllTextAsync(System.IO.Path.Combine(_dir, "Calc.cs"), TestContext.Current.CancellationToken);
        Assert.EndsWith("        public int Mul(int a)\n        {\n            return a * 2;\n        }\n    }\n}\n", text, StringComparison.Ordinal);
        Assert.InRange(result.Range.StartLine, 14, 15);
        Assert.Equal(3, result.Range.EndLine - result.Range.StartLine);
        Assert.False(string.IsNullOrEmpty(result.NewHash));
        Assert.Equal([VirtualPath], _index.DirtyPaths);
    }

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldAcceptNoExpectedHash_WhenBeforeOrAfter()
    {
        var fs = await SeedAsync();
        using var tool = new NodeInsertTool(fs, _pool, _index);

        Assert.Contains("no expected_hash", tool.Description, StringComparison.Ordinal);
        await CallAsync(tool, "Demo.Calc.Sub(int, int)", "after", "public int Neg(int a) { return -a; }");

        var text = await File.ReadAllTextAsync(System.IO.Path.Combine(_dir, "Calc.cs"), TestContext.Current.CancellationToken);
        var sub = text.IndexOf("return a - b;", StringComparison.Ordinal);
        var neg = text.IndexOf("\n        public int Neg(int a) { return -a; }\n", StringComparison.Ordinal);
        var add = text.IndexOf("public int Add(int a)", StringComparison.Ordinal);
        Assert.True(sub >= 0 && sub < neg && neg < add, text);
    }

    [Fact]
    [Trait("BR", "RT-40")]
    public async Task ShouldRefuse_WhenWriteRightMissing()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(Original);
        var roFs = new FakeRightsFileSystemService().AddMount("/workspace", FileAccessRights.Read).AddFile(VirtualPath, bytes);
        using var tool = new NodeInsertTool(roFs, _pool, _index);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => CallAsync(tool, "Demo.Calc", "inside", "public int Mul() { return 1; }"));

        Assert.Contains("does not grant the right", ex.Message, StringComparison.Ordinal);
        Assert.Equal(bytes, await roFs.TryReadAllBytesAsync(VirtualPath, TestContext.Current.CancellationToken));
        Assert.Empty(_index.DirtyPaths);
    }
}
