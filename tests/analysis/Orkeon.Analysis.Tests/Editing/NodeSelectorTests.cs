using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests.Editing;

public sealed class NodeSelectorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nodesel").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private async Task<StructuralFile> LoadAsync(string source, TreeSitterParserPool pool)
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/Sample.cs", source, CancellationToken.None);
        return await StructuralFile.LoadAsync(fs, pool, "/workspace/Sample.cs");
    }

    private const string Overloads =
        "namespace Ns\n{\n    public class Type\n    {\n        public void Add(int x) { }\n\n        public void Add(string s) { }\n    }\n}\n";

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldResolveMethodBySignature_WhenOverloadsExist()
    {
        using var pool = new TreeSitterParserPool();
        var file = await LoadAsync(Overloads, pool);

        var node = file.Resolve(NodeSelector.Parse("Ns.Type.Add(int)"));

        const string expected = "public void Add(int x) { }";
        Assert.Equal("Ns.Type.Add(int)", node.Selector);
        Assert.Equal("method_declaration", node.Kind);
        Assert.Equal(expected, node.Text);
        var bytes = Encoding.UTF8.GetBytes(Overloads);
        Assert.Equal(expected, Encoding.UTF8.GetString(bytes, node.Range.StartByte, node.Range.EndByte - node.Range.StartByte));
        Assert.Equal(5, node.Range.StartLine); // 1-based, inclusive
        Assert.Equal(5, node.Range.EndLine);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expected))), node.Sha256);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldListCandidates_WhenSelectorIsAmbiguousOverload()
    {
        using var pool = new TreeSitterParserPool();
        var file = await LoadAsync(Overloads, pool);

        var ex = Assert.Throws<AmbiguousSelectorException>(() => file.Resolve(NodeSelector.Parse("Ns.Type.Add")));

        Assert.Equal(["Ns.Type.Add(int)", "Ns.Type.Add(string)"], ex.Candidates.Order(StringComparer.Ordinal).ToArray());
    }

    // Path syntax chosen: "@ts:" + root kind, then repeated "/<i>/<kind>" where <i> is the 0-based index
    // of the node among the NAMED children of its parent (comments count) and <kind> its tree-sitter type.
    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldResolveByTreeSitterPath_WhenSelectorStartsWithAt()
    {
        const string source =
            "public class Type\n{\n    /// <summary>Doc.</summary>\n    [Obsolete]\n    public void M() { }\n}\n";
        using var pool = new TreeSitterParserPool();
        var file = await LoadAsync(source, pool);

        var node = file.Resolve(NodeSelector.Parse(
            "@ts:compilation_unit/0/class_declaration/2/declaration_list/1/method_declaration"));

        Assert.Equal("method_declaration", node.Kind);
        Assert.Equal("/// <summary>Doc.</summary>\n    [Obsolete]\n    public void M() { }", node.Text);
        Assert.Equal(3, node.Range.StartLine);
        Assert.Equal(5, node.Range.EndLine);
    }
}
