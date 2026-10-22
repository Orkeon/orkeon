using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests.Editing;

public sealed class StructuralFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("structfile").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldRefuseByName_WhenExtensionHasNoGrammar()
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/x.xyz", "hello", CancellationToken.None);
        using var pool = new TreeSitterParserPool();

        var ex = await Assert.ThrowsAsync<UnsupportedExtensionException>(
            () => StructuralFile.LoadAsync(fs, pool, "/workspace/x.xyz"));

        Assert.Equal(".xyz", ex.Extension);
        Assert.Contains(".xyz", ex.Message);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public void ShouldPublishShippedGrammars_WhenListed()
    {
        var shipped = LanguageRegistry.Shipped;

        Assert.Contains(shipped, g => g.Language == "csharp" && g.Extensions.Contains(".cs"));
        Assert.Contains(shipped, g => g.Language == "typescript" && g.Extensions.Contains(".ts"));
        Assert.Contains(shipped, g => g.Extensions.Contains(".md"));
    }

    private const string Doc = "# A\nintro\n## B\nbody b\n### B1\nsub\n## C\nbody c\n# D\nbody d\n";
    private const string Section = "## B\nbody b\n### B1\nsub";

    private static string Sha(byte[] raw, int start, int end) =>
        Convert.ToHexString(SHA256.HashData(raw.AsSpan(start, end - start))).ToLowerInvariant();

    private async Task<StructuralFile> LoadBytesAsync(string name, byte[] content)
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllBytesAsync("/workspace/" + name, content, CancellationToken.None);
        using var pool = new TreeSitterParserPool();
        return await StructuralFile.LoadAsync(fs, pool, "/workspace/" + name);
    }

    [Theory]
    [InlineData(".py")]
    [InlineData(".rs")]
    [Trait("BR", "RT-44")]
    public async Task ShouldRefuseByName_WhenGrammarHasNoNodeKinds(string extension)
    {
        var fs = new DiskBackedFileSystemService(_dir, "/workspace");
        await fs.WriteAllTextAsync("/workspace/x" + extension, "x", CancellationToken.None);
        using var pool = new TreeSitterParserPool();

        var ex = await Assert.ThrowsAsync<UnsupportedExtensionException>(
            () => StructuralFile.LoadAsync(fs, pool, "/workspace/x" + extension));

        Assert.Equal(extension, ex.Extension);

        await fs.WriteAllTextAsync("/workspace/ok.cs", "class A { }", CancellationToken.None);
        await fs.WriteAllTextAsync("/workspace/ok.ts", "class A { }", CancellationToken.None);
        Assert.NotNull(await StructuralFile.LoadAsync(fs, pool, "/workspace/ok.cs"));
        Assert.NotNull(await StructuralFile.LoadAsync(fs, pool, "/workspace/ok.ts"));
    }

    [Fact]
    [Trait("BR", "RT-38")]
    public async Task ShouldKeepBomAndCrlfBytes_WhenHashingNode()
    {
        const string text = "namespace N\r\n{\r\n    public class C\r\n    {\r\n        public int M(int a)\r\n        {\r\n            return a;\r\n        }\r\n    }\r\n}\r\n";
        var raw = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        var file = await LoadBytesAsync("Bom.cs", raw);

        var node = file.Resolve(NodeSelector.Parse("N.C.M(int)"));

        var expected = Encoding.UTF8.GetBytes("public int M(int a)\r\n        {\r\n            return a;\r\n        }");
        var start = raw.AsSpan().IndexOf(expected);
        Assert.Equal(start, node.Range.StartByte);
        Assert.Equal(start + expected.Length, node.Range.EndByte);
        Assert.Equal(Sha(raw, start, start + expected.Length), node.Sha256);
        Assert.Equal(Encoding.UTF8.GetString(expected), node.Text);
    }

    [Fact]
    [Trait("BR", "RT-38")]
    public async Task ShouldConvertOffsetsToBytes_WhenLeadingCommentHasMultiByteCharacter()
    {
        const string member = "// café € éé €€\n        public int M(int a)\n        {\n            return a;\n        }";
        const string text = "namespace N\n{\n    public class C\n    {\n        " + member + "\n    }\n}\n";
        var raw = Encoding.UTF8.GetBytes(text);
        var file = await LoadBytesAsync("Multi.cs", raw);

        var node = file.Resolve(NodeSelector.Parse("N.C.M(int)"));

        var expected = Encoding.UTF8.GetBytes(member);
        var start = raw.AsSpan().IndexOf(expected);
        Assert.Equal(start, node.Range.StartByte);
        Assert.Equal(start + expected.Length, node.Range.EndByte);
        Assert.Equal(Sha(raw, start, start + expected.Length), node.Sha256);
        Assert.Equal(member, node.Text);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldOutlineMarkdownHeadings_WhenFileIsMarkdown()
    {
        var file = await LoadBytesAsync("d.md", Encoding.UTF8.GetBytes(Doc));

        var outline = file.Outline();

        Assert.Equal(["A", "A/B", "A/B/B1", "A/C", "D"], outline.Select(n => n.Selector).ToArray());
        var b = outline.Single(n => n.Selector == "A/B");
        var start = Doc.IndexOf(Section, StringComparison.Ordinal);
        Assert.Equal(start, b.Range.StartByte);
        Assert.Equal(start + Section.Length, b.Range.EndByte);
        Assert.Equal(3, b.Range.StartLine);
        Assert.Equal(6, b.Range.EndLine);
        var a = outline.Single(n => n.Selector == "A");
        var b1 = outline.Single(n => n.Selector == "A/B/B1");
        Assert.Equal(5, b1.Range.StartLine);
        Assert.Equal(6, b1.Range.EndLine);
        Assert.Equal(1, a.Range.StartLine);
        Assert.Equal(8, a.Range.EndLine);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldResolveSectionPath_WhenSelectorIsMarkdownPath()
    {
        var raw = Encoding.UTF8.GetBytes(Doc);
        var file = await LoadBytesAsync("d.md", raw);

        var node = file.Resolve(NodeSelector.Parse("A/B"));

        var start = Doc.IndexOf(Section, StringComparison.Ordinal);
        Assert.Equal(Section, node.Text);
        Assert.Equal(start, node.Range.StartByte);
        Assert.Equal(start + Section.Length, node.Range.EndByte);
        Assert.Equal(Sha(raw, start, start + Section.Length), node.Sha256);

        var ex = Assert.Throws<SelectorNotFoundException>(() => file.Resolve(NodeSelector.Parse("A/Z")));
        Assert.Contains("A/B", ex.Suggestions);
    }

    private const string Dup = "# A\n## Notes\nfirst\n## Notes\nsecond\n";

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldThrowAmbiguousWithOrdinalCandidates_WhenMarkdownSiblingsShareName()
    {
        var file = await LoadBytesAsync("dup.md", Encoding.UTF8.GetBytes(Dup));

        var ex = Assert.Throws<AmbiguousSelectorException>(() => file.Resolve(NodeSelector.Parse("A/Notes")));

        Assert.Equal(["A/Notes[1]", "A/Notes[2]"], ex.Candidates.ToArray());
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldResolveSecondSection_WhenOrdinalSelectorGiven()
    {
        var file = await LoadBytesAsync("dup.md", Encoding.UTF8.GetBytes(Dup));

        Assert.Equal("## Notes\nfirst", file.Resolve(NodeSelector.Parse("A/Notes[1]")).Text);
        Assert.Equal("## Notes\nsecond", file.Resolve(NodeSelector.Parse("A/Notes[2]")).Text);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldResolveEachWithoutOrdinal_WhenSameNameUnderDifferentParents()
    {
        var file = await LoadBytesAsync("two.md", Encoding.UTF8.GetBytes("# A\n## Notes\nin a\n# B\n## Notes\nin b\n"));

        Assert.Equal("## Notes\nin a", file.Resolve(NodeSelector.Parse("A/Notes")).Text);
        Assert.Equal("## Notes\nin b", file.Resolve(NodeSelector.Parse("B/Notes")).Text);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldResolveSingleHeading_WhenNameIsUnique()
    {
        var file = await LoadBytesAsync("d.md", Encoding.UTF8.GetBytes(Doc));

        Assert.Equal(Section, file.Resolve(NodeSelector.Parse("A/B")).Text);
        Assert.Equal(Section, file.Resolve(NodeSelector.Parse("A/B[1]")).Text);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldListDuplicatesWithOrdinal_WhenOutlineHasSiblingsOfSameName()
    {
        var file = await LoadBytesAsync("dup.md", Encoding.UTF8.GetBytes(Dup));

        var selectors = file.Outline().Select(n => n.Selector).ToArray();

        Assert.Equal(["A", "A/Notes[1]", "A/Notes[2]"], selectors);
        foreach (var selector in selectors)
            Assert.Equal(selector, file.Resolve(NodeSelector.Parse(selector)).Selector);
    }

    [Theory]
    [InlineData("A/Notes[3]")]
    [InlineData("A/Notes[0]")]
    [Trait("BR", "RT-44")]
    public async Task ShouldReportNotFound_WhenOrdinalExceedsSiblings(string selector)
    {
        var file = await LoadBytesAsync("dup.md", Encoding.UTF8.GetBytes(Dup));

        Assert.Throws<SelectorNotFoundException>(() => file.Resolve(NodeSelector.Parse(selector)));
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldResolveEachChildUnderSlashHeading_WhenHeadingContainsSlash()
    {
        var file = await LoadBytesAsync("ci.md", Encoding.UTF8.GetBytes("# CI/CD\n## Build\nb\n## Deploy\nd\n"));

        var selectors = file.Outline().Select(n => n.Selector).ToArray();

        Assert.Equal(["CI/CD", "CI/CD/Build", "CI/CD/Deploy"], selectors);
        Assert.Equal("## Build\nb", file.Resolve(NodeSelector.Parse("CI/CD/Build")).Text);
        Assert.Equal("## Deploy\nd", file.Resolve(NodeSelector.Parse("CI/CD/Deploy")).Text);
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldTakeOrdinals_WhenDuplicateSectionsSitUnderSlashHeading()
    {
        var file = await LoadBytesAsync("cin.md", Encoding.UTF8.GetBytes("# CI/CD\n## Notes\nfirst\n## Notes\nsecond\n"));

        var selectors = file.Outline().Select(n => n.Selector).ToArray();

        Assert.Equal(["CI/CD", "CI/CD/Notes[1]", "CI/CD/Notes[2]"], selectors);
        Assert.Equal("## Notes\nfirst", file.Resolve(NodeSelector.Parse("CI/CD/Notes[1]")).Text);
        Assert.Equal("## Notes\nsecond", file.Resolve(NodeSelector.Parse("CI/CD/Notes[2]")).Text);
        var ex = Assert.Throws<AmbiguousSelectorException>(() => file.Resolve(NodeSelector.Parse("CI/CD/Notes")));
        Assert.Equal(["CI/CD/Notes[1]", "CI/CD/Notes[2]"], ex.Candidates.ToArray());
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldOutlineAndResolveOrdinalOnParent_WhenParentHeadingIsDuplicated()
    {
        var file = await LoadBytesAsync("pp.md", Encoding.UTF8.GetBytes("# A\n## Notes\nfirst\n# A\n## Notes\nsecond\n"));

        var selectors = file.Outline().Select(n => n.Selector).ToArray();

        Assert.Equal(["A[1]", "A[1]/Notes", "A[2]", "A[2]/Notes"], selectors);
        Assert.Equal("## Notes\nfirst", file.Resolve(NodeSelector.Parse("A[1]/Notes")).Text);
        Assert.Equal("## Notes\nsecond", file.Resolve(NodeSelector.Parse("A[2]/Notes")).Text);
        Assert.Throws<AmbiguousSelectorException>(() => file.Resolve(NodeSelector.Parse("A/Notes")));
    }

    [Fact]
    [Trait("BR", "RT-44")]
    public async Task ShouldResolveLiteralHeading_WhenHeadingNameLooksLikeOrdinal()
    {
        var literal = await LoadBytesAsync("lit.md", Encoding.UTF8.GetBytes("# A\n## x[2]\nlit\n"));

        Assert.Equal(["A", "A/x[2]"], literal.Outline().Select(n => n.Selector).ToArray());
        Assert.Equal("## x[2]\nlit", literal.Resolve(NodeSelector.Parse("A/x[2]")).Text);

        var plain = await LoadBytesAsync("ord.md", Encoding.UTF8.GetBytes("# A\n## x\none\n## x\ntwo\n"));

        Assert.Equal("## x\ntwo", plain.Resolve(NodeSelector.Parse("A/x[2]")).Text);
    }
}
