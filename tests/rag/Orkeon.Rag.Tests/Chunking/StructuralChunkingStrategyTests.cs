using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Chunking;

namespace Orkeon.Rag.Tests.Chunking;

/// <summary>
/// Tests for <see cref="StructuralChunkingStrategy"/>, including the non-regression
/// cases ported from the historical <c>MdxSearchTool.ChunkByHeadings</c> suite
/// (RAG-02/C3).
/// </summary>
public class StructuralChunkingStrategyTests
{
    private readonly StructuralChunkingStrategy _strategy = new();

    [Fact]
    public void Name_IsStructural()
    {
        Assert.Equal("structural", _strategy.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n   ")]
    public void Chunk_WhitespaceOnlyContent_ReturnsEmpty(string content)
    {
        var chunks = _strategy.Chunk(ChunkingTestHelper.Doc(content), new ChunkingOptions());

        Assert.Empty(chunks);
    }

    // Ported from MDXSearchToolTests.ChunkByHeadings_ShouldSplitAtHeadingBoundaries
    [Fact]
    public void Chunk_MarkdownHeadings_SplitsAtHeadingBoundaries()
    {
        var doc = ChunkingTestHelper.Doc(
            "# Intro\n\nSome text.\n\n## Section A\n\nContent A.\n\n## Section B\n\nContent B.");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 5000 });

        Assert.True(chunks.Count >= 3, $"Expected at least 3 heading-based chunks, got {chunks.Count}");
        Assert.Contains("# Intro", chunks[0].Content, StringComparison.Ordinal);
        Assert.Contains("## Section A", chunks[1].Content, StringComparison.Ordinal);
        Assert.Contains("## Section B", chunks[2].Content, StringComparison.Ordinal);
    }

    // Ported from MDXSearchToolTests.ChunkByHeadings_ShouldSubSplitLargeSections
    [Fact]
    public void Chunk_OversizedSection_IsSubSplit()
    {
        var longBody = string.Join(". ",
            Enumerable.Range(1, 40).Select(i => $"Sentence number {i} in this long section"));
        var doc = ChunkingTestHelper.Doc($"## Large Section\n\n{longBody}");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 200, Overlap = 0 });

        Assert.True(chunks.Count > 1, $"Expected sub-splitting of large section, got {chunks.Count} chunk(s)");
        Assert.All(chunks, c => Assert.Equal(
            "## Large Section",
            c.Metadata[ChunkingStrategyBase.HeadingMetadataKey]));
    }

    [Fact]
    public void Chunk_PreambleBeforeFirstHeading_IsItsOwnChunkWithoutHeadingMetadata()
    {
        var doc = ChunkingTestHelper.Doc("Leading intro text before any heading.\n\n# First\n\nBody.");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 5000 });

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Leading intro text before any heading.", chunks[0].Content);
        Assert.False(chunks[0].Metadata.ContainsKey(ChunkingStrategyBase.HeadingMetadataKey));
        Assert.Contains("# First", chunks[1].Content, StringComparison.Ordinal);
        Assert.Equal("# First", chunks[1].Metadata[ChunkingStrategyBase.HeadingMetadataKey]);
    }

    [Fact]
    public void Chunk_NoHeadings_ReturnsWholeTextAsSingleChunk()
    {
        var doc = ChunkingTestHelper.Doc("Just a plain paragraph.\n\nAnd another one.");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 5000 });

        var chunk = Assert.Single(chunks);
        Assert.Equal(doc.Content, chunk.Content);
    }

    [Fact]
    public void Chunk_HeadingLevels_RecognizesOneToSixHashesOnly()
    {
        var doc = ChunkingTestHelper.Doc(
            "###### Deep heading\n\nBody deep.\n\n####### Not a heading\n\nBody not.");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 5000 });

        var chunk = Assert.Single(chunks);
        Assert.Equal("###### Deep heading", chunk.Metadata[ChunkingStrategyBase.HeadingMetadataKey]);
        Assert.Contains("####### Not a heading", chunk.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Chunk_Offsets_MatchDocumentContent()
    {
        var doc = ChunkingTestHelper.Doc(
            "# Title\n\nIntro body text.\n\n## Sub\n\nMore body text here with words.");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 30, Overlap = 0 });

        Assert.NotEmpty(chunks);
        ChunkingTestHelper.AssertOffsetsCoherent(doc, chunks);
    }
}
