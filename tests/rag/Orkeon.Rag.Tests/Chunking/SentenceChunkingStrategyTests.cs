using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Chunking;

namespace Orkeon.Rag.Tests.Chunking;

/// <summary>
/// Tests for <see cref="SentenceChunkingStrategy"/>, including the non-regression
/// cases ported from the historical <c>SentenceChunkerTests</c> (Infrastructure) suite
/// (RAG-02/C3).
/// </summary>
public class SentenceChunkingStrategyTests
{
    private readonly SentenceChunkingStrategy _strategy = new();

    [Fact]
    public void Name_IsSentence()
    {
        Assert.Equal("sentence", _strategy.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \n \n ")]
    public void Chunk_WhitespaceOnlyContent_ReturnsEmpty(string content)
    {
        var chunks = _strategy.Chunk(ChunkingTestHelper.Doc(content), new ChunkingOptions());

        Assert.Empty(chunks);
    }

    // Ported from SentenceChunkerTests.ShouldReturnSingleChunk_WhenTextHasSingleSentence
    [Fact]
    public void Chunk_SingleSentence_ReturnsSingleChunk()
    {
        var doc = ChunkingTestHelper.Doc("This is a single sentence. ");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 500 });

        var chunk = Assert.Single(chunks);
        Assert.Contains("This is a single sentence.", chunk.Content, StringComparison.Ordinal);
    }

    // Ported from SentenceChunkerTests.ShouldGroupBySize_WhenTextHasMultipleSentences
    [Fact]
    public void Chunk_MultipleSentences_GroupsBySizeOnSentenceBoundaries()
    {
        var doc = ChunkingTestHelper.Doc(
            "First sentence. Second sentence. Third sentence. Fourth sentence. Fifth sentence. ");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 40, Overlap = 0 });

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(
            c.Content.Contains(". ", StringComparison.Ordinal) || c.Content.TrimEnd().EndsWith('.'),
            "chunks should contain complete sentences"));
    }

    // Ported from SentenceChunkerTests.ShouldRespectChunkSize_WhenChunking
    // (strengthened: chunks of multiple sentences never exceed MaxChunkSize).
    [Fact]
    public void Chunk_ShortSentences_RespectsMaxChunkSize()
    {
        var doc = ChunkingTestHelper.Doc("Short. Another short. Yet another. One more. And final. ");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 30, Overlap = 0 });

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Content.Length <= 30,
            $"chunk of length {c.Content.Length} exceeds MaxChunkSize"));
    }

    // Ported from SentenceChunkerTests.ShouldSplitCorrectly_WhenTextHasDifferentSentenceEndings
    [Fact]
    public void Chunk_MixedSentenceEndings_KeepsAllSentences()
    {
        var doc = ChunkingTestHelper.Doc("Statement one. Question two? Exclamation three! Statement four. ");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 40, Overlap = 0 });

        Assert.True(chunks.Count > 1);
        var allContent = string.Concat(chunks.Select(c => c.Content));
        Assert.Contains("Statement one.", allContent, StringComparison.Ordinal);
        Assert.Contains("Question two?", allContent, StringComparison.Ordinal);
        Assert.Contains("Exclamation three!", allContent, StringComparison.Ordinal);
    }

    // Ported from SentenceChunkerTests.ShouldReturnOneChunk_WhenTextIsLongSingleSentence.
    // Documented behavior: this strategy never splits inside a sentence.
    [Fact]
    public void Chunk_OversizedSingleSentence_IsEmittedWhole()
    {
        var text = "This is one very long sentence without a period at the end";
        var doc = ChunkingTestHelper.Doc(text);

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 20 });

        var chunk = Assert.Single(chunks);
        Assert.Equal(text, chunk.Content);
    }

    [Fact]
    public void Chunk_WithOverlap_RepeatsTrailingSentences()
    {
        var doc = ChunkingTestHelper.Doc(
            "Sentence one. Sentence two. Sentence three. Sentence four. Sentence five. Sentence six. Sentence seven. Sentence eight.");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 50, Overlap = 20 });

        Assert.True(chunks.Count > 1);
        Assert.Contains(
            Enumerable.Range(0, chunks.Count - 1),
            i => chunks[i + 1].StartOffset < chunks[i].EndOffset);
    }

    [Fact]
    public void Chunk_Offsets_MatchDocumentContent()
    {
        var doc = ChunkingTestHelper.Doc("First sentence. Second sentence. Third sentence. ");

        var chunks = _strategy.Chunk(doc, new ChunkingOptions { MaxChunkSize = 30, Overlap = 0 });

        Assert.NotEmpty(chunks);
        ChunkingTestHelper.AssertOffsetsCoherent(doc, chunks);
    }
}
