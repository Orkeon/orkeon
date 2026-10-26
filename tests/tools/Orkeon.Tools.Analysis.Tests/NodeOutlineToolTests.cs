using Orkeon.Domain.Tools.Protocol;
using System.Text;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Analysis.Tests;

public sealed class NodeOutlineToolTests : IDisposable
{
    private const int Types = 4;
    private const int MethodsPerType = 1250;

    private readonly string _dir = Directory.CreateTempSubdirectory("nodeoutline").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    [Trait("BR", "RT-38")]
    public async Task ShouldListMembersWithRanges_WhenFileHasTwentyThousandLines()
    {
        var sb = new StringBuilder("namespace Big\n{\n");
        for (var t = 0; t < Types; t++)
        {
            sb.Append("    public class Holder").Append(t).Append('\n').Append("    {\n");
            for (var m = 0; m < MethodsPerType; m++)
            {
                sb.Append("        public int Op").Append(m).Append("(int x)\n")
                  .Append("        {\n")
                  .Append("            return x + ").Append(m).Append(";\n")
                  .Append("        }\n");
            }
            sb.Append("    }\n");
        }
        sb.Append("}\n");
        var source = sb.ToString();
        var bytes = Encoding.UTF8.GetBytes(source);
        var lineAtByte = new int[bytes.Length + 1];
        lineAtByte[0] = 1;
        for (var i = 0; i < bytes.Length; i++)
            lineAtByte[i + 1] = lineAtByte[i] + (bytes[i] == (byte)'\n' ? 1 : 0);
        Assert.True(source.Count(c => c == '\n') >= 20_000);

        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/Big.cs", source, CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        using var tool = new NodeOutlineTool(fs, pool);

        var resp = await tool.ExecuteTypedForTest(new NodeOutlineRequest { Path = "/workspace/Big.cs" }, CancellationToken.None);

        var methods = resp.Members.Where(e => e.Selector.Contains(".Op", StringComparison.Ordinal)).ToList();
        Assert.Equal(Types * MethodsPerType, methods.Count);
        Assert.Contains(resp.Members, e => e.Selector == "Big.Holder0.Op0(int)");
        Assert.Contains(resp.Members, e => e.Selector == "Big.Holder3.Op1249(int)");
        for (var i = 1; i < resp.Members.Count; i++)
            Assert.True(resp.Members[i].StartByte >= resp.Members[i - 1].StartByte);
        Assert.All(resp.Members, e =>
        {
            Assert.InRange(e.StartByte, 0, bytes.Length);
            Assert.InRange(e.EndByte, e.StartByte + 1, bytes.Length);
            Assert.Equal(lineAtByte[e.StartByte], e.StartLine);
            Assert.Equal(lineAtByte[e.EndByte - 1], e.EndLine);
        });
        Assert.All(methods, e => Assert.Equal(e.StartLine + 3, e.EndLine));
    }

    [Fact]
    [Trait("BR", "RT-38")]
    public async Task ShouldOutlineTypeScriptFile_WhenExtensionIsTs()
    {
        const string source = "class Greeter {\n  greet(name: string) {\n    return name;\n  }\n}\n\nfunction helper() {\n  return 1;\n}\n";
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/g.ts", source, CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        using var tool = new NodeOutlineTool(fs, pool);

        var resp = await tool.ExecuteTypedForTest(new NodeOutlineRequest { Path = "/workspace/g.ts" }, CancellationToken.None);

        var greet = Assert.Single(resp.Members, e => e.Selector.Contains("greet", StringComparison.Ordinal));
        var helper = Assert.Single(resp.Members, e => e.Selector.Contains("helper", StringComparison.Ordinal));
        Assert.Equal(2, greet.StartLine);
        Assert.Equal(4, greet.EndLine);
        Assert.Equal("greet(name: string) {\n    return name;\n  }", source[greet.StartByte..greet.EndByte]);
        Assert.Equal(7, helper.StartLine);
        Assert.Equal(9, helper.EndLine);
        Assert.Equal("function helper() {\n  return 1;\n}", source[helper.StartByte..helper.EndByte]);
    }

    [Theory]
    [Trait("BR", "CH-200")]
    [InlineData("")]
    [InlineData("  ")]
    public void ShouldNameThePathArgument_WhenPathIsMissing(string path)
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace") { WorkingDirectory = "/workspace" };
        using var pool = new TreeSitterParserPool();
        using var tool = new NodeOutlineTool(fs, pool);

        var error = tool.ValidateTypedForTest(new NodeOutlineRequest { Path = path });

        Assert.Equal("path is required: the absolute path of the file, e.g. /workspace/src/stats.js", error);
    }

    [Fact]
    [Trait("BR", "CH-199")]
    public async Task ShouldOutlineFile_WhenPathIsRelativeToWorkingDirectory()
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace") { WorkingDirectory = "/workspace" };
        await fs.WriteAllTextAsync("/workspace/src/stats.js", "function sum(xs) {\n  return 0;\n}\n", CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        using var tool = new NodeOutlineTool(fs, pool);

        var resp = await tool.ExecuteTypedForTest(new NodeOutlineRequest { Path = "src/stats.js" }, CancellationToken.None);

        Assert.Contains(resp.Members, e => e.Selector.StartsWith("sum", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async Task ShouldRefuse_WhenRelativePathEscapesAgentRoot()
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace") { WorkingDirectory = "/workspace" };
        await fs.WriteAllTextAsync("/workspace/wt/p2/src/stats.js", "function sum(xs) {\n  return 0;\n}\n", CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        using var tool = new NodeOutlineTool(fs, pool);

        string? error;
        await using (Orkeon.Domain.Execution.AgentRunScope.Begin("agent-1", "dev", "/workspace/wt/p1"))
            error = tool.ValidateTypedForTest(new NodeOutlineRequest { Path = "../p2/src/stats.js" });

        Assert.NotNull(error);
        Assert.Contains("outside the agent working root", error, StringComparison.Ordinal);
        Assert.Contains("/workspace/wt/p2/src/stats.js", error, StringComparison.Ordinal);
    }
}
