using Orkeon.Rag.Abstractions;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Tools.Rag.Tests;

/// <summary>
/// Glob semantics shared by every ingestion surface (<c>rag_ingest</c>, CLI,
/// scripting): pattern splitting, regex translation, VFS expansion, dedup and
/// deterministic ordering.
/// </summary>
public class SourceGlobExpanderTests
{
    private static readonly string[] MixedLocations =
        ["/workspace/docs/a.md", "/workspace/docs/*.md", "https://example.org/faq"];

    private static readonly string[] ExpectedMixedExpansion =
        ["/workspace/docs/a.md", "/workspace/docs/b.md", "https://example.org/faq"];

    [Theory]
    [InlineData("/workspace/docs/**/*.md", "/workspace/docs", "**/*.md")]
    [InlineData("/workspace/*.md", "/workspace", "*.md")]
    [InlineData("/a/b/c.txt", "/a/b/c.txt", "")]
    [InlineData("/docs/report-?.pdf", "/docs", "report-?.pdf")]
    public void SplitAtFirstWildcardSegment_SeparatesLiteralBase_FromPattern(
        string pattern, string expectedRoot, string expectedRelative)
    {
        var (root, relative) = SourceGlobExpander.SplitAtFirstWildcardSegment(pattern);

        Assert.Equal(expectedRoot, root);
        Assert.Equal(expectedRelative, relative);
    }

    [Theory]
    [InlineData("**/*.md", "a.md", true)]
    [InlineData("**/*.md", "sub/deep/a.md", true)]
    [InlineData("**/*.md", "a.txt", false)]
    [InlineData("*.md", "a.md", true)]
    [InlineData("*.md", "sub/a.md", false)]
    [InlineData("report-?.pdf", "report-1.pdf", true)]
    [InlineData("report-?.pdf", "report-12.pdf", false)]
    public void GlobToRegex_MatchesLikeAGlob(string glob, string candidate, bool expected)
    {
        Assert.Equal(expected, SourceGlobExpander.GlobToRegex(glob).IsMatch(candidate));
    }

    [Fact]
    public void HasWildcard_DetectsGlobCharacters()
    {
        Assert.True(SourceGlobExpander.HasWildcard("/docs/*.md"));
        Assert.True(SourceGlobExpander.HasWildcard("/docs/a?.md"));
        Assert.False(SourceGlobExpander.HasWildcard("/docs/a.md"));
    }

    [Fact]
    public async Task ExpandAsync_KeepsPlainLocations_ExpandsGlobs_Deduplicates_AndSorts()
    {
        var fs = new FakeFileSystemService()
            .AddMount("/workspace")
            .AddFile("/workspace/docs/b.md", "b")
            .AddFile("/workspace/docs/a.md", "a")
            .AddFile("/workspace/docs/notes.txt", "t");

        var result = await SourceGlobExpander.ExpandAsync(
            fs, MixedLocations, TestContext.Current.CancellationToken);

        Assert.Equal(ExpectedMixedExpansion, result.Select(s => s.Location));
    }
}
