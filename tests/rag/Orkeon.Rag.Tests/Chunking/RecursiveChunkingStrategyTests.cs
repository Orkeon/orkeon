using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Chunking;

namespace Orkeon.Rag.Tests.Chunking;

/// <summary>
/// Tests for <see cref="RecursiveChunkingStrategy"/>, including the non-regression
/// cases ported from the historical <c>RecursiveTextChunkerTests</c> (Infrastructure)
/// and <c>TextChunkerTests</c> (Tools.Abstractions) suites (RAG-02/C3).
/// </summary>
public class RecursiveChunkingStrategyTests
{
    private readonly RecursiveChunkingStrategy _strategy = new();

    [Fact]
    public void Name_IsRecursive()
    {
        Assert.Equal("recursive", _strategy.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void Chunk_WhitespaceOnlyContent_ReturnsEmpty(string content)
    {
        var chunks = _strategy.Chunk(ChunkingTestHelper.Doc(content), new ChunkingOptions());

        Assert.Empty(chunks);
    }

    // Ported from RecursiveTextChunkerTests.ShouldReturnSingleChunk_WhenTextIsShort
    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunkWithExactOffsets()
    {
        var doc = ChunkingTestHelper.Doc("Short text.");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 500, Overlap = 0 });

        var chunk = Assert.Single(chunks);
        Assert.Equal("Short text.", chunk.Content);
        Assert.Equal(0, chunk.StartOffset);
        Assert.Equal(doc.Content.Length, chunk.EndOffset);
    }

    // Ported from RecursiveTextChunkerTests.ShouldReturnMultipleChunks_WhenTextIsLong
    [Fact]
    public void Chunk_LongText_ReturnsMultipleChunks()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 20).Select(i => $"Paragraph {i} with some text content."));
        var doc = ChunkingTestHelper.Doc(text);

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 100, Overlap = 10 });

        Assert.True(chunks.Count > 1);
    }

    // Ported from RecursiveTextChunkerTests.ShouldRespectChunkSize_WhenChunking
    // (strengthened: the canonical strategy guarantees a hard cap).
    [Fact]
    public void Chunk_WordSoup_RespectsMaxChunkSize()
    {
        var text = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"word{i}"));
        var doc = ChunkingTestHelper.Doc(text);

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 100, Overlap = 0 });

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Content.Length <= 100,
            $"chunk of length {c.Content.Length} exceeds MaxChunkSize"));
    }

    // Ported from RecursiveTextChunkerTests.ShouldRespectOverlap_WhenChunkOverlapIsSet
    [Fact]
    public void Chunk_WithOverlap_ProducesContentContinuity()
    {
        var text = string.Join(" ", Enumerable.Range(1, 40).Select(i => $"word{i:D3}"));
        var doc = ChunkingTestHelper.Doc(text);

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 50, Overlap = 20 });

        Assert.True(chunks.Count > 1);
        Assert.Contains(
            Enumerable.Range(0, chunks.Count - 1),
            i => chunks[i + 1].StartOffset < chunks[i].EndOffset);
    }

    // Ported from RecursiveTextChunkerTests.ShouldSplitOnParagraphs_WhenTextHasParagraphBreaks
    [Fact]
    public void Chunk_ParagraphBreaks_SplitsOnParagraphs()
    {
        var text = "First paragraph with enough text.\n\nSecond paragraph with enough text.\n\nThird paragraph with enough text.";
        var doc = ChunkingTestHelper.Doc(text);

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 50, Overlap = 0 });

        Assert.True(chunks.Count > 1);
        Assert.Contains("First paragraph", chunks[0].Content, StringComparison.Ordinal);
    }

    // Ported from RecursiveTextChunkerTests.ShouldSplitOnSentences_WhenTextHasSentenceBreaks
    [Fact]
    public void Chunk_SentenceBreaks_SplitsOnSentences()
    {
        var text = "First sentence. Second sentence. Third sentence. Fourth sentence. Fifth sentence.";
        var doc = ChunkingTestHelper.Doc(text);

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 40, Overlap = 0 });

        Assert.True(chunks.Count > 1);
    }

    // Ported from RecursiveTextChunkerTests.ShouldStillProduceOutput_WhenChunkSizeIsVerySmall
    [Fact]
    public void Chunk_TinyMaxChunkSize_KeepsAllText()
    {
        var doc = ChunkingTestHelper.Doc("Hello world");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 5, Overlap = 0 });

        Assert.True(chunks.Count > 1);
        var combined = string.Concat(chunks.Select(c => c.Content));
        Assert.Contains("Hello", combined, StringComparison.Ordinal);
        Assert.Contains("world", combined, StringComparison.Ordinal);
    }

    // Ported from TextChunkerTests (Tools.Abstractions): small paragraphs are now MERGED
    // up to MaxChunkSize — the documented consolidation semantics (RAG-02/C3) — instead
    // of one-chunk-per-paragraph.
    [Fact]
    public void Chunk_SmallParagraphs_AreMergedUpToMaxChunkSize()
    {
        var doc = ChunkingTestHelper.Doc("First paragraph.\n\nSecond paragraph.\n\nThird paragraph.");

        var merged = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 100, Overlap = 0 });
        var split = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 20, Overlap = 0 });

        var single = Assert.Single(merged);
        Assert.Equal(doc.Content, single.Content);

        Assert.Equal(3, split.Count);
        Assert.Equal("First paragraph.", split[0].Content);
        Assert.Equal("Second paragraph.", split[1].Content);
        Assert.Equal("Third paragraph.", split[2].Content);
    }

    // Ported from TextChunkerTests.ChunkContent_ShouldSplitLargeParagraph_BySentenceBoundaries
    [Fact]
    public void Chunk_LargeParagraph_IsSplitBySentences()
    {
        var doc = ChunkingTestHelper.Doc(
            "Sentence one is here. Sentence two is here. Sentence three is here. Sentence four is here.");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 40, Overlap = 0 });

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c.Content)));
    }

    [Fact]
    public void Chunk_Offsets_MatchDocumentContent()
    {
        var text = "Alpha paragraph text.\n\nBeta paragraph with more words in it. Another sentence here.\n\nGamma.";
        var doc = ChunkingTestHelper.Doc(text);

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 40, Overlap = 0 });

        Assert.NotEmpty(chunks);
        ChunkingTestHelper.AssertOffsetsCoherent(doc, chunks);
    }

    [Fact]
    public void Chunk_InheritsDocumentMetadata()
    {
        var doc = ChunkingTestHelper.Doc("Some content.") with
        {
            Metadata = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty
                .Add("origin", "unit-test")
        };

        var chunks = _strategy.Chunk(doc, new ChunkingOptions());

        var chunk = Assert.Single(chunks);
        Assert.Equal("unit-test", chunk.Metadata["origin"]);
    }

    [Fact]
    public void Chunk_CustomSeparators_AreHonored()
    {
        var strategy = new RecursiveChunkingStrategy(["|", ""]);
        var doc = ChunkingTestHelper.Doc("aaaa|bbbb|cccc|dddd");

        var chunks = strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 5, Overlap = 0 });

        Assert.True(chunks.Count >= 4);
        Assert.Equal("aaaa|", chunks[0].Content);
        ChunkingTestHelper.AssertOffsetsCoherent(doc, chunks);
    }
}
