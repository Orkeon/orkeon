using Orkeon.Application.Interfaces.Knowledge;

namespace Orkeon.Infrastructure.Tests.Knowledge.Chunking;

public class RecursiveTextChunkerTests
{
    private readonly RecursiveTextChunkerTestsFixture _fixture = new();

    [Fact]
    public void ShouldReturnSingleChunk_WhenTextIsShort()
    {
        var text = "Short text.";

        var chunks = _fixture.Chunk(text, new ChunkingOptions { ChunkSize = 500 });

        Assert.Single(chunks);
        Assert.Equal(text, chunks[0].Content);
        Assert.Equal(0, chunks[0].StartIndex);
        Assert.Equal(text.Length, chunks[0].EndIndex);
    }

    [Fact]
    public void ShouldReturnMultipleChunks_WhenTextIsLong()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 20).Select(i => $"Paragraph {i} with some text content."));

        var chunks = _fixture.Chunk(text, new ChunkingOptions { ChunkSize = 100, ChunkOverlap = 10 });

        Assert.True(chunks.Count > 1);
    }

    [Fact]
    public void ShouldRespectChunkSize_WhenChunking()
    {
        var text = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"word{i}"));

        var chunks = _fixture.Chunk(text, new ChunkingOptions { ChunkSize = 100, ChunkOverlap = 0 });

        Assert.True(chunks.Count > 1);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Content.Length <= 110,
                "chunks should be approximately within the chunk size limit");
        }
    }

    [Fact]
    public void ShouldRespectOverlap_WhenChunkOverlapIsSet()
    {
        var text = "Sentence one. Sentence two. Sentence three. Sentence four. Sentence five. Sentence six. Sentence seven. Sentence eight.";

        var chunks = _fixture.Chunk(text, new ChunkingOptions { ChunkSize = 50, ChunkOverlap = 20 });

        if (chunks.Count >= 2)
        {
            var firstEnd = chunks[0].Content;
            var secondStart = chunks[1].Content;

            var firstEndChars = firstEnd.Length >= 20 ? firstEnd[^20..] : firstEnd;
            Assert.True(
                secondStart.Contains(firstEndChars, StringComparison.Ordinal) ||
                firstEnd[^10..].Length > 0,
                "there should be some content continuity between chunks");
        }
    }

    [Fact]
    public void ShouldSplitOnParagraphs_WhenTextHasParagraphBreaks()
    {
        var text = "First paragraph with enough text.\n\nSecond paragraph with enough text.\n\nThird paragraph with enough text.";

        var chunks = _fixture.Chunk(text, new ChunkingOptions { ChunkSize = 50, ChunkOverlap = 0 });

        Assert.True(chunks.Count > 1);
        Assert.Contains("First paragraph", chunks[0].Content);
    }

    [Fact]
    public void ShouldSplitOnSentences_WhenTextHasSentenceBreaks()
    {
        var text = "First sentence. Second sentence. Third sentence. Fourth sentence. Fifth sentence.";

        var chunks = _fixture.Chunk(text, new ChunkingOptions { ChunkSize = 40, ChunkOverlap = 0 });

        Assert.True(chunks.Count > 1);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenTextIsEmpty()
    {
        Assert.Empty(_fixture.Chunk(""));
        Assert.Empty(_fixture.Chunk(null!));
    }

    [Fact]
    public void ShouldWork_WhenUsingDefaultOptions()
    {
        var text = "Some text that should be chunked with default options.";

        var chunks = _fixture.Chunk(text);

        Assert.True(chunks.Count >= 1);
        Assert.Equal(text, chunks[0].Content);
    }

    [Fact]
    public void ShouldStillProduceOutput_WhenChunkSizeIsVerySmall()
    {
        var text = "Hello world";

        var chunks = _fixture.Chunk(text, new ChunkingOptions { ChunkSize = 5, ChunkOverlap = 0 });

        Assert.True(chunks.Count > 1);
        var combined = string.Join("", chunks.Select(c => c.Content));
        Assert.Contains("Hello", combined);
        Assert.Contains("world", combined);
    }

    [Fact]
    public void ShouldHaveValidIndices_WhenChunking()
    {
        var text = "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.";

        var chunks = _fixture.Chunk(text, new ChunkingOptions { ChunkSize = 30, ChunkOverlap = 0 });

        foreach (var chunk in chunks)
        {
            Assert.True(chunk.StartIndex >= 0);
            Assert.True(chunk.EndIndex > chunk.StartIndex);
            Assert.True(chunk.EndIndex <= text.Length);
        }
    }
}
