using System.Security.Cryptography;
using System.Text;
using Orkeon.Analysis.Core.Editing;
using Orkeon.Analysis.Core.Editing.Intent;
using Orkeon.Analysis.Tests.Doubles;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests.Editing.Intent;

public sealed class MarkdownIntentOperationsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mdintent").FullName;
    private readonly TreeSitterParserPool _pool = new();
    private readonly DiskBackedFileSystemService _disk;
    private readonly FakeCountingFileSystemService _counting;

    public MarkdownIntentOperationsTests()
    {
        _disk = new DiskBackedFileSystemService(_dir, "/workspace");
        _counting = new FakeCountingFileSystemService(_disk);
    }

    public void Dispose()
    {
        _pool.Dispose();
        Directory.Delete(_dir, true);
    }

    private async Task SeedAsync(string name, string content)
        => await _disk.WriteAllBytesAsync("/workspace/" + name, Encoding.UTF8.GetBytes(content), CancellationToken.None);

    private async Task<string> ReadAsync(string name)
        => Encoding.UTF8.GetString((await _disk.TryReadAllBytesAsync("/workspace/" + name, CancellationToken.None))!);

    private async Task<EditOutcome> ApplyAsync(string file, IEditOperation operation)
        => await new StructuralEditEngine(_counting, _pool).ApplyAsync("/workspace/" + file, [operation]);

    [Fact]
    [Trait("BR", "RT-43")]
    public async Task ShouldCreateSectionOnceAtRightLevel_WhenEnsureSectionRepeated()
    {
        await SeedAsync("doc.md", "# Plan\n\n## Notes\n");

        var first = await ApplyAsync("doc.md", new EnsureSectionOperation("Plan/Anchors"));
        var afterFirst = await ReadAsync("doc.md");
        var writes = _counting.Writes.Count;
        var second = await ApplyAsync("doc.md", new EnsureSectionOperation("Plan/Anchors"));

        Assert.True(first.Applied);
        Assert.Equal("# Plan\n\n## Notes\n\n## Anchors\n", afterFirst);
        Assert.False(second.Applied);
        Assert.Equal(afterFirst, await ReadAsync("doc.md"));
        Assert.Equal(writes, _counting.Writes.Count);
    }

    [Fact]
    [Trait("BR", "RT-43")]
    public async Task ShouldCreateMissingParentHeadings_WhenPathIsDeep()
    {
        await SeedAsync("doc.md", "# Intro\n");

        var outcome = await ApplyAsync("doc.md", new EnsureSectionOperation("A/B/C"));

        Assert.True(outcome.Applied);
        Assert.Equal("# Intro\n\n# A\n\n## B\n\n### C\n", await ReadAsync("doc.md"));
    }

    [Fact]
    [Trait("BR", "RT-43")]
    public async Task ShouldInsertBeforeNextHeadingOfSameOrHigherLevel_WhenInsertUnderHeading()
    {
        const string source = "# Plan\n\n## A\n\nA text\n\n### A1\n\nA1 text\n\n## B\n\nB text\n\n# Other\n\nO text\n";
        await SeedAsync("a.md", source);
        await SeedAsync("b.md", source);

        var inner = await ApplyAsync("a.md", new InsertUnderHeadingOperation(NodeSelector.Parse("Plan/A"), "Added"));
        var outer = await ApplyAsync("b.md", new InsertUnderHeadingOperation(NodeSelector.Parse("Plan"), "Added"));

        Assert.True(inner.Applied);
        Assert.Equal("# Plan\n\n## A\n\nA text\n\n### A1\n\nA1 text\n\nAdded\n\n## B\n\nB text\n\n# Other\n\nO text\n", await ReadAsync("a.md"));
        Assert.True(outer.Applied);
        Assert.Equal("# Plan\n\n## A\n\nA text\n\n### A1\n\nA1 text\n\n## B\n\nB text\n\nAdded\n\n# Other\n\nO text\n", await ReadAsync("b.md"));
    }

    [Fact]
    [Trait("BR", "RT-43")]
    public async Task ShouldReplaceOnlyNthMermaidFence_WhenSectionHasSeveral()
    {
        const string source = "# Doc\n\n## Sec\n\n```mermaid\ngraph A\n```\n\n```mermaid\ngraph B\n```\n\n```csharp\nvar x = 1;\n```\n\n```mermaid\ngraph C\n```\n\n### Sub\n\n~~~mermaid\ngraph D\n~~~\n\n## Other\n\n```mermaid\ngraph E\n```\n";
        await SeedAsync("one.md", source);
        await SeedAsync("three.md", source);

        var second = await ApplyAsync("one.md", new ReplaceMermaidOperation(NodeSelector.Parse("Doc/Sec"), 1, "graph B2"));
        var fourth = await ApplyAsync("three.md", new ReplaceMermaidOperation(NodeSelector.Parse("Doc/Sec"), 3, "graph D2"));

        Assert.True(second.Applied);
        Assert.Equal(source.Replace("graph B\n", "graph B2\n", StringComparison.Ordinal), await ReadAsync("one.md"));
        Assert.True(fourth.Applied);
        Assert.Equal(source.Replace("graph D\n", "graph D2\n", StringComparison.Ordinal), await ReadAsync("three.md"));
    }

    [Fact]
    [Trait("BR", "RT-43")]
    public async Task ShouldKeepCrlfAndNoFinalNewline_WhenSectionEnsuredAtEndOfCrlfFileWithoutFinalNewline()
    {
        await SeedAsync("a.md", "# Plan\r\n\r\n## Notes");
        await SeedAsync("b.md", "# Plan\r\n\r\n## Notes");

        await ApplyAsync("a.md", new EnsureSectionOperation("Plan/Anchors"));
        await ApplyAsync("b.md", new EnsureSectionOperation("Plan/X/Y"));

        Assert.Equal("# Plan\r\n\r\n## Notes\r\n\r\n## Anchors", await ReadAsync("a.md"));
        Assert.Equal("# Plan\r\n\r\n## Notes\r\n\r\n## X\r\n\r\n### Y", await ReadAsync("b.md"));
    }

    [Fact]
    [Trait("BR", "RT-43")]
    public async Task ShouldKeepCrlfAndNoFinalNewline_WhenTextInsertedUnderHeadingAtEndOfCrlfFileWithoutFinalNewline()
    {
        await SeedAsync("doc.md", "# Plan\r\n\r\ntext");

        await ApplyAsync("doc.md", new InsertUnderHeadingOperation(NodeSelector.Parse("Plan"), "Line one\nLine two"));

        Assert.Equal("# Plan\r\n\r\ntext\r\n\r\nLine one\r\nLine two", await ReadAsync("doc.md"));
    }

    [Fact]
    [Trait("BR", "RT-43")]
    public async Task ShouldKeepCrlf_WhenMermaidReplacedInCrlfFile()
    {
        await SeedAsync("a.md", "# Doc\r\n\r\n```mermaid\r\ngraph A\r\n```\r\n\r\nend\r\n");
        await SeedAsync("b.md", "# Doc\r\n\r\n```mermaid\r\ngraph A\r\n```");

        await ApplyAsync("a.md", new ReplaceMermaidOperation(NodeSelector.Parse("Doc"), 0, "graph TD\n  A --> B"));
        await ApplyAsync("b.md", new ReplaceMermaidOperation(NodeSelector.Parse("Doc"), 0, "graph TD\n  A --> B"));

        Assert.Equal("# Doc\r\n\r\n```mermaid\r\ngraph TD\r\n  A --> B\r\n```\r\n\r\nend\r\n", await ReadAsync("a.md"));
        Assert.Equal("# Doc\r\n\r\n```mermaid\r\ngraph TD\r\n  A --> B\r\n```", await ReadAsync("b.md"));
    }

    [Fact]
    [Trait("BR", "RT-43")]
    public async Task ShouldKeepLfAndNoFinalNewline_WhenSectionEnsuredAtEndOfLfFileWithoutFinalNewline()
    {
        await SeedAsync("doc.md", "# Plan\n\n## Notes");

        await ApplyAsync("doc.md", new EnsureSectionOperation("Plan/Anchors"));

        Assert.Equal("# Plan\n\n## Notes\n\n## Anchors", await ReadAsync("doc.md"));
    }

    [Fact]
    [Trait("BR", "RT-43")]
    public async Task ShouldRefuse_WhenSectionHasFewerMermaidBlocks()
    {
        const string source = "# Doc\n\n## Sec\n\n```mermaid\ngraph A\n```\n\n```csharp\nvar x = 1;\n```\n";
        await SeedAsync("doc.md", source);

        await Assert.ThrowsAsync<SelectorNotFoundException>(
            () => ApplyAsync("doc.md", new ReplaceMermaidOperation(NodeSelector.Parse("Doc/Sec"), 1, "graph B")));

        Assert.Equal(source, await ReadAsync("doc.md"));
    }

    [Fact]
    [Trait("BR", "RT-43")]
    public void ShouldKeepCodeFenceNodes_WhenMarkdownIsParsed()
    {
        const string source = "# Doc\n\n```mermaid\ngraph TD\n```\n\n## Code\n\n```csharp\nvar x = 1;\n```\n\n~~~\nplain\n~~~\n";
        var file = StructuralFile.FromBytes(_pool, Encoding.UTF8.GetBytes(source), "/workspace/doc.md");

        var fences = file.CodeFences();

        Assert.Equal(3, fences.Count);
        Assert.All(fences, f => Assert.Equal("CodeFence", f.Kind));
        Assert.StartsWith("```mermaid", fences[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("```csharp", fences[1].Text, StringComparison.Ordinal);
        Assert.StartsWith("~~~", fences[2].Text, StringComparison.Ordinal);
        Assert.True(fences[0].Range.StartByte < fences[1].Range.StartByte && fences[1].Range.StartByte < fences[2].Range.StartByte);
        Assert.Equal(["Doc", "Doc/Code"], file.Outline().Select(n => n.Selector).ToArray());
    }

    [Fact]
    [Trait("BR", "RT-43")]
    public async Task ShouldCreateChildOneLevelBelowResolvedParent_WhenParentIsNotAtDepthLevel()
    {
        await SeedAsync("doc.md", "## Notes\n");

        await ApplyAsync("doc.md", new EnsureSectionOperation("Notes/Todo"));
        var afterFirst = await ReadAsync("doc.md");
        var second = await ApplyAsync("doc.md", new EnsureSectionOperation("Notes/Todo"));

        Assert.Equal("## Notes\n\n### Todo\n", afterFirst);
        Assert.False(second.Applied);
        Assert.Equal(1, (await ReadAsync("doc.md")).Split("Todo").Length - 1);
    }

    [Fact]
    [Trait("BR", "RT-43")]
    public async Task ShouldReportRangeOfExistingSection_WhenEnsureSectionIsNoOp()
    {
        const string source = "# Plan\n\n## Anchors\n";
        await SeedAsync("doc.md", source);

        var outcome = await ApplyAsync("doc.md", new EnsureSectionOperation("Plan/Anchors"));

        var slice = Encoding.UTF8.GetBytes(source).AsSpan(outcome.Range.StartByte, outcome.Range.EndByte - outcome.Range.StartByte);
        Assert.False(outcome.Applied);
        Assert.StartsWith("## Anchors", Encoding.UTF8.GetString(slice), StringComparison.Ordinal);
        Assert.Equal(3, outcome.Range.StartLine);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(slice)), outcome.NewHash);
    }

    [Theory]
    [Trait("BR", "RT-43")]
    [InlineData("Notes\n-----\n", "### Todo")]
    [InlineData("Notes\n=====\n", "## Todo")]
    public async Task ShouldCreateChildOneLevelBelowParent_WhenParentIsSetextHeading(string source, string expectedHeading)
    {
        await SeedAsync("doc.md", source);

        var first = await ApplyAsync("doc.md", new EnsureSectionOperation("Notes/Todo"));
        var afterFirst = await ReadAsync("doc.md");
        var second = await ApplyAsync("doc.md", new EnsureSectionOperation("Notes/Todo"));

        Assert.True(first.Applied);
        Assert.Equal(source + "\n" + expectedHeading + "\n", afterFirst);
        Assert.False(second.Applied);
        Assert.Equal(1, (await ReadAsync("doc.md")).Split("Todo").Length - 1);
    }

    [Fact]
    [Trait("BR", "RT-43")]
    public async Task ShouldCreateChildOneLevelBelowParent_WhenParentIsIndentedAtxHeading()
    {
        await SeedAsync("doc.md", "  ## Notes\n");

        var first = await ApplyAsync("doc.md", new EnsureSectionOperation("Notes/Todo"));
        var afterFirst = await ReadAsync("doc.md");
        var second = await ApplyAsync("doc.md", new EnsureSectionOperation("Notes/Todo"));

        Assert.True(first.Applied);
        Assert.Equal("  ## Notes\n\n### Todo\n", afterFirst);
        Assert.False(second.Applied);
        Assert.Equal(1, (await ReadAsync("doc.md")).Split("Todo").Length - 1);
    }

    [Fact]
    [Trait("BR", "RT-43")]
    public async Task ShouldCreateChildOneLevelBelowParent_WhenParentIsAtxHeading()
    {
        await SeedAsync("doc.md", "### Notes\n");

        var first = await ApplyAsync("doc.md", new EnsureSectionOperation("Notes/Todo"));
        var afterFirst = await ReadAsync("doc.md");
        var second = await ApplyAsync("doc.md", new EnsureSectionOperation("Notes/Todo"));

        Assert.True(first.Applied);
        Assert.Equal("### Notes\n\n#### Todo\n", afterFirst);
        Assert.False(second.Applied);
        Assert.Equal(1, (await ReadAsync("doc.md")).Split("Todo").Length - 1);
    }

    [Theory]
    [Trait("BR", "RT-43")]
    [InlineData("# S\n\n```mermaid\nold", "# S\n\n```mermaid\nNEW\n```")]
    [InlineData("# S\n\n```mermaid", "# S\n\n```mermaid\nNEW\n```")]
    [InlineData("# S\n\n```mermaid\nold\n", "# S\n\n```mermaid\nNEW\n```\n")]
    public async Task ShouldReplaceBodyToEndOfFile_WhenMermaidFenceIsUnclosed(string source, string expected)
    {
        await SeedAsync("doc.md", source);

        await ApplyAsync("doc.md", new ReplaceMermaidOperation(NodeSelector.Parse("S"), 0, "NEW"));

        Assert.Equal(expected, await ReadAsync("doc.md"));
    }

    [Fact]
    [Trait("BR", "R17")]
    public async Task ShouldAppendClosingFence_WhenMermaidBlockIsUnclosed()
    {
        await SeedAsync("doc.md", "# S\n\n```mermaid\nold");

        await ApplyAsync("doc.md", new ReplaceMermaidOperation(NodeSelector.Parse("S"), 0, "graph TD\n  A --> B"));

        Assert.Equal("# S\n\n```mermaid\ngraph TD\n  A --> B\n```", await ReadAsync("doc.md"));
    }

    [Fact]
    [Trait("BR", "R17")]
    public async Task ShouldUseTildeFence_WhenUnclosedBlockOpensWithTildes()
    {
        await SeedAsync("doc.md", "# S\n\n~~~mermaid\nold");

        await ApplyAsync("doc.md", new ReplaceMermaidOperation(NodeSelector.Parse("S"), 0, "graph TD\n  A --> B"));

        Assert.Equal("# S\n\n~~~mermaid\ngraph TD\n  A --> B\n~~~", await ReadAsync("doc.md"));
    }

    [Fact]
    [Trait("BR", "R17")]
    public async Task ShouldKeepOneClosingFence_WhenMermaidBlockIsClosed()
    {
        await SeedAsync("doc.md", "# S\n\n```mermaid\nold\n```\n\nend\n");

        await ApplyAsync("doc.md", new ReplaceMermaidOperation(NodeSelector.Parse("S"), 0, "graph TD\n  A --> B"));

        var text = await ReadAsync("doc.md");
        Assert.Equal("# S\n\n```mermaid\ngraph TD\n  A --> B\n```\n\nend\n", text);
        Assert.Equal(2, text.Split("```").Length - 1);
    }
}
