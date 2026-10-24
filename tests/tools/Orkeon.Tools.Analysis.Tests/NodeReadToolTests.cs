using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Analysis.Tests;

public sealed class NodeReadToolTests : IDisposable
{
    private const string Small =
        "namespace Demo\n{\n    public class Calc\n    {\n"
        + "        public int Sub(int a, int b)\n        {\n            return a - b;\n        }\n\n"
        + "        [Obsolete(\"use Mul\")]\n        /// <summary>Adds two numbers.</summary>\n"
        + "        public int Add(int a)\n        {\n            return a + 1;\n        }\n    }\n}\n";

    private readonly string _dir = Directory.CreateTempSubdirectory("noderead").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private async Task<NodeReadResponse> ReadAsync(string content, string selector)
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/Calc.cs", content, CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        using var tool = new NodeReadTool(fs, pool);
        return await tool.ExecuteTypedForTest(
            new NodeReadRequest { Path = "/workspace/Calc.cs", Selector = selector }, CancellationToken.None);
    }

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    [Fact]
    [Trait("BR", "RT-38")]
    public async Task ShouldReturnOnlyMemberTextRangeAndHash_WhenSelectorResolves()
    {
        var resp = await ReadAsync(Small, "Demo.Calc.Sub(int, int)");

        const string expected = "public int Sub(int a, int b)\n        {\n            return a - b;\n        }";
        Assert.Equal(expected, resp.Text);
        Assert.DoesNotContain("Add", resp.Text, StringComparison.Ordinal);
        Assert.Equal(Small.IndexOf(expected, StringComparison.Ordinal), resp.StartByte);
        Assert.Equal(resp.StartByte + expected.Length, resp.EndByte);
        Assert.Equal(5, resp.StartLine);
        Assert.Equal(8, resp.EndLine);
        Assert.Equal(Sha(expected), resp.Sha256);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldIncludeLeadingAttributesAndComments_WhenReadingNode()
    {
        var resp = await ReadAsync(Small, "Demo.Calc.Add(int)");

        Assert.StartsWith("[Obsolete(\"use Mul\")]", resp.Text, StringComparison.Ordinal);
        Assert.Contains("/// <summary>Adds two numbers.</summary>", resp.Text, StringComparison.Ordinal);
        Assert.EndsWith("return a + 1;\n        }", resp.Text, StringComparison.Ordinal);
        Assert.Equal(Small.IndexOf("[Obsolete", StringComparison.Ordinal), resp.StartByte);
        Assert.Equal(10, resp.StartLine);
        Assert.Equal(Sha(resp.Text), resp.Sha256);
    }

    [Fact]
    [Trait("BR", "RT-38")]
    public async Task ShouldReturnUncutText_WhenNodeExceedsDefaultToolCut()
    {
        var body = new StringBuilder();
        for (var i = 0; i < 600; i++)
            body.Append("            total += ").Append(i).Append(" * 3;\n");
        var member = "public int Big(int total)\n        {\n" + body + "            return total;\n        }";
        var source = "namespace Demo\n{\n    public class Huge\n    {\n        " + member + "\n    }\n}\n";
        Assert.True(member.Length > AgentDefaults.MaxToolResultLength);

        var resp = await ReadAsync(source, "Demo.Huge.Big(int)");

        Assert.Equal(member, resp.Text);
        Assert.Equal(Sha(member), resp.Sha256);
        Assert.True(AgentDefaults.ResolveMaxToolResultLength("node_read") >= member.Length);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public void ShouldDescribeSelectorFormsInFieldSchema_WhenToolSchemaIsRead()
    {
        var schema = typeof(NodeReadRequest).GetProperty(nameof(NodeReadRequest.Selector))!
            .GetCustomAttribute<FieldSchemaAttribute>();

        Assert.NotNull(schema);
        var description = schema.Description;
        Assert.NotNull(description);
        Assert.Contains("Ns.Type.Member(paramTypes)", description, StringComparison.Ordinal);
        Assert.Contains("Section/Sub", description, StringComparison.Ordinal);
        Assert.Contains("@ts:", description, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldReadMarkdownSection_WhenExtensionIsMd()
    {
        const string doc = "# A\nintro\n## B\nbody b\n### B1\nsub\n## C\nbody c\n# D\nbody d\n";
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/d.md", doc, CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        using var tool = new NodeReadTool(fs, pool);

        var resp = await tool.ExecuteTypedForTest(
            new NodeReadRequest { Path = "/workspace/d.md", Selector = "A/B" }, CancellationToken.None);

        const string section = "## B\nbody b\n### B1\nsub";
        Assert.Equal(section, resp.Text);
        Assert.Equal(doc.IndexOf(section, StringComparison.Ordinal), resp.StartByte);
        Assert.Equal(resp.StartByte + section.Length, resp.EndByte);
        Assert.Equal(Sha(section), resp.Sha256);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public void ShouldNameOrdinalForm_WhenSelectorFieldSchemaIsRead()
    {
        var schema = typeof(NodeReadRequest).GetProperty(nameof(NodeReadRequest.Selector))!
            .GetCustomAttribute<FieldSchemaAttribute>();

        Assert.NotNull(schema);
        var description = schema.Description;
        Assert.NotNull(description);
        Assert.Contains("[2]", description, StringComparison.Ordinal);
        Assert.Contains("ordinal", description, StringComparison.OrdinalIgnoreCase);
    }

    private const string TwoNotes = "# A\nintro\n## Notes\ntext one\n## Notes\ntext two\n";

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldReadSecondSection_WhenOrdinalSelectorGivenOnMarkdown()
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/n.md", TwoNotes, CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        using var tool = new NodeReadTool(fs, pool);

        var resp = await tool.ExecuteTypedForTest(
            new NodeReadRequest { Path = "/workspace/n.md", Selector = "A/Notes[2]" }, CancellationToken.None);

        const string section = "## Notes\ntext two";
        Assert.Equal(section, resp.Text);
        Assert.Equal(TwoNotes.LastIndexOf(section, StringComparison.Ordinal), resp.StartByte);
        Assert.Equal(resp.StartByte + section.Length, resp.EndByte);
        Assert.Equal(Sha(section), resp.Sha256);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldListOrdinalCandidates_WhenMarkdownSelectorIsAmbiguous()
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/n.md", TwoNotes, CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        using var tool = new NodeReadTool(fs, pool);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => tool.ExecuteTypedForTest(
            new NodeReadRequest { Path = "/workspace/n.md", Selector = "A/Notes" }, CancellationToken.None));

        Assert.Contains("A/Notes[1]", ex.Message, StringComparison.Ordinal);
        Assert.Contains("A/Notes[2]", ex.Message, StringComparison.Ordinal);
    }
}
