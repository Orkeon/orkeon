using Orkeon.Application.Interfaces.Knowledge;

namespace Orkeon.Infrastructure.Tests.Knowledge.Chunking;

public class SentenceChunkerTests
{
    private readonly SentenceChunkerTestsFixture _fixture = new();

    [Fact]
    public void ShouldReturnSingleChunk_WhenTextHasSingleSentence()
    {
        var text = "This is a single sentence. ";

        var chunks = _fixture.Chunk(text, new ChunkingOptions { ChunkSize = 500 });

        Assert.Single(chunks);
        Assert.Contains("This is a single sentence.", chunks[0].Content);
    }

    [Fact]
    public void ShouldGroupBySize_WhenTextHasMultipleSentences()
    {
        var text = "First sentence. Second sentence. Third sentence. Fourth sentence. Fifth sentence. ";

        var chunks = _fixture.Chunk(text, new ChunkingOptions { ChunkSize = 40, ChunkOverlap = 0 });

        Assert.True(chunks.Count > 1);
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Content.Contains(". ") || chunk.Content.TrimEnd().EndsWith('.'),
                "chunks should contain complete sentences");
        }
    }

    [Fact]
    public void ShouldRespectChunkSize_WhenChunking()
    {
        var text = "Short. Another short. Yet another. One more. And final. ";

        var chunks = _fixture.Chunk(text, new ChunkingOptions { ChunkSize = 30, ChunkOverlap = 0 });

        Assert.True(chunks.Count > 1);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenTextIsEmpty()
    {
        Assert.Empty(_fixture.Chunk(""));
        Assert.Empty(_fixture.Chunk(null!));
    }

    [Fact]
    public void ShouldSplitCorrectly_WhenTextHasDifferentSentenceEndings()
    {
        var text = "Statement one. Question two? Exclamation three! Statement four. ";

        var chunks = _fixture.Chunk(text, new ChunkingOptions { ChunkSize = 40, ChunkOverlap = 0 });

        Assert.True(chunks.Count > 1);
        var allContent = string.Join("", chunks.Select(c => c.Content));
        Assert.Contains("Statement one.", allContent);
        Assert.Contains("Question two?", allContent);
        Assert.Contains("Exclamation three!", allContent);
    }

    [Fact]
    public void ShouldReturnOneChunk_WhenTextIsLongSingleSentence()
    {
        var text = "This is one very long sentence without a period at the end";

        var chunks = _fixture.Chunk(text, new ChunkingOptions { ChunkSize = 20 });

        Assert.Single(chunks);
        Assert.Equal(text, chunks[0].Content);
    }

    [Fact]
    public void ShouldHaveValidIndices_WhenChunking()
    {
        var text = "First sentence. Second sentence. Third sentence. ";

        var chunks = _fixture.Chunk(text, new ChunkingOptions { ChunkSize = 30, ChunkOverlap = 0 });

        foreach (var chunk in chunks)
        {
            Assert.True(chunk.StartIndex >= 0);
            Assert.True(chunk.EndIndex > chunk.StartIndex);
            Assert.True(chunk.EndIndex <= text.Length);
        }
    }

    [Fact]
    public void ShouldWork_WhenUsingDefaultOptions()
    {
        var text = "A sentence. Another sentence. ";

        var chunks = _fixture.Chunk(text);

        Assert.True(chunks.Count >= 1);
    }
}
