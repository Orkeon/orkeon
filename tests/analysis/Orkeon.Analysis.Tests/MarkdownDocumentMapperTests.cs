using System.Text;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests;

public class MarkdownDocumentMapperTests
{
    private const string Path = "/docs/a.md";

    private static List<RaggableNode> Sections(IReadOnlyList<RaggableNode> nodes) =>
        nodes.Where(n => n.Kind == UniversalNodeKind.Section).ToList();

    private static RaggableNode Node(string id, string fqn, string path, UniversalNodeKind kind) => new()
    {
        Id = id,
        Kind = kind,
        Name = fqn,
        Fqn = fqn,
        VirtualFilePath = path,
        Range = new NodeRange(0, 1, 0, 0, 0),
        Level = kind == UniversalNodeKind.Module ? NodeLevel.L2_Module : NodeLevel.L3_Symbol,
        Language = "csharp",
        SourceSnippet = "",
        Sha256 = "x",
    };

    [Fact]
    [Trait("BR", "RT-29")]
    public void ShouldEmitSectionNodesWithRanges_WhenMarkdownHasTwoHeadingLevels()
    {
        const string md = "# Title\ntext\n## Sub-one\ntext";
        var sections = Sections(new MarkdownDocumentMapper().Map(Path, md));

        Assert.Equal(2, sections.Count);
        Assert.All(sections, s => Assert.True(s.Range.StartByte < s.Range.EndByte));
        var title = sections.Single(s => s.Name == "Title");
        var sub = sections.Single(s => s.Name == "Sub-one");
        Assert.Equal(0, title.Range.StartByte);
        Assert.Equal(Encoding.UTF8.GetByteCount(md), title.Range.EndByte);
        Assert.Equal(Encoding.UTF8.GetByteCount("# Title\ntext\n"), sub.Range.StartByte);
        Assert.Equal(Encoding.UTF8.GetByteCount(md), sub.Range.EndByte);
    }

    [Fact]
    [Trait("BR", "RT-29")]
    public void ShouldNestSubsectionUnderSection_WhenHeadingLevelsNest()
    {
        var sections = Sections(new MarkdownDocumentMapper().Map(Path, "# A\n## B\n### C\n## D"));

        Assert.Equal(
            [$"{Path}::A", $"{Path}::A/B", $"{Path}::A/B/C", $"{Path}::A/D"],
            sections.Select(s => s.Fqn).ToArray());
    }

    [Fact]
    [Trait("BR", "RT-29")]
    public void ShouldStoreByteOffsets_WhenHeadingHasAccentedCharacters()
    {
        const string prefix = "# Café résumé\nline\n";
        var sections = Sections(new MarkdownDocumentMapper().Map(Path, prefix + "## Next"));

        var next = sections.Single(s => s.Name == "Next");
        Assert.Equal(Encoding.UTF8.GetByteCount(prefix), next.Range.StartByte);
        Assert.NotEqual(prefix.Length, next.Range.StartByte);
    }

    [Fact]
    [Trait("BR", "RT-29")]
    public void ShouldTagMermaidFence_WhenFenceLanguageIsMermaid()
    {
        var sut = new MarkdownDocumentMapper();
        var mermaid = sut.Map(Path, "```mermaid\ngraph TD\n```").Single(n => n.Kind == UniversalNodeKind.CodeFence);
        var csharp = sut.Map(Path, "```csharp\nvar x = 1;\n```").Single(n => n.Kind == UniversalNodeKind.CodeFence);

        Assert.True(mermaid.Tags.ContainsKey("mermaid"));
        Assert.True(csharp.Tags.ContainsKey("csharp"));
        Assert.False(csharp.Tags.ContainsKey("mermaid"));
    }

    [Fact]
    [Trait("BR", "RT-29")]
    public async Task ShouldResolveLinkToFileNode_WhenTargetIsRelativePath()
    {
        var store = new InMemoryRaggableStore(
            [Node("file-b", "/docs/b.md", "/docs/b.md", UniversalNodeKind.Module)], [], new FakeFileSystemService());
        var sut = new MarkdownDocumentMapper();
        var nodes = sut.Map(Path, "[see](./b.md) and [x](./missing.md)");

        var resolved = await sut.ResolveLinksAsync(nodes, Path, store, CancellationToken.None);

        var links = resolved.Where(n => n.Kind == UniversalNodeKind.Link).ToList();
        var good = links.Single(l => l.Tags.ContainsKey("target:./b.md"));
        Assert.Contains(good.Tags.Keys, k => k is "resolved:file-b" or "resolved:/docs/b.md");
        var missing = links.Single(l => l.Tags.ContainsKey("target:./missing.md"));
        Assert.DoesNotContain(missing.Tags.Keys, k => k.StartsWith("resolved:", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("BR", "RT-29")]
    public async Task ShouldResolveLinkToSymbol_WhenTargetNamesALocalName()
    {
        var store = new InMemoryRaggableStore(
            [Node("sym-os", "/src/Orders.cs::OrderService", "/src/Orders.cs", UniversalNodeKind.Class)], [], new FakeFileSystemService());
        var sut = new MarkdownDocumentMapper();
        var nodes = sut.Map(Path, "[the service](OrderService)");

        var resolved = await sut.ResolveLinksAsync(nodes, Path, store, CancellationToken.None);

        var link = resolved.Single(n => n.Kind == UniversalNodeKind.Link);
        Assert.True(link.Tags.ContainsKey("target:OrderService"));
        Assert.Contains(link.Tags.Keys, k => k is "resolved:sym-os" or "resolved:/src/Orders.cs::OrderService");
    }

    [Theory]
    [Trait("BR", "RT-29")]
    [InlineData("---\nplain text\n---\n# H")]
    [InlineData("---\n- a\n---\n# H")]
    [InlineData("---\ntitle: [x\n---\n# H")]
    public void ShouldEmitFrontMatterNodeWithoutKeys_WhenFrontMatterIsNotAYamlMapping(string content)
    {
        var nodes = new MarkdownDocumentMapper().Map(Path, content);

        var front = nodes.Single(n => n.Kind == UniversalNodeKind.FrontMatter);
        Assert.Empty(front.Tags);
        Assert.Equal("H", Assert.Single(Sections(nodes)).Name);
    }

    [Fact]
    [Trait("BR", "RT-29")]
    public void ShouldEmitFrontMatterNodeWithKeys_WhenDocumentStartsWithYaml()
    {
        const string fm = "---\ntitle: T\ntags: [a]\n---\n";
        var nodes = new MarkdownDocumentMapper().Map(Path, fm + "# H");

        var front = nodes.Single(n => n.Kind == UniversalNodeKind.FrontMatter);
        Assert.True(front.Tags.ContainsKey("title"));
        Assert.True(front.Tags.ContainsKey("tags"));
        Assert.Equal(0, front.Range.StartByte);
        Assert.Equal(Encoding.UTF8.GetByteCount(fm.TrimEnd('\n')), front.Range.EndByte);
        Assert.Equal("H", Assert.Single(Sections(nodes)).Name);
    }
}
