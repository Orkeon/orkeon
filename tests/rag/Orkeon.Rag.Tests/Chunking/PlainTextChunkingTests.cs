using Orkeon.Rag.Chunking;
using Orkeon.Rag.Tests.Doubles;

namespace Orkeon.Rag.Tests.Chunking;

/// <summary>Tests for the <see cref="PlainTextChunking"/> helpers used by the search facades.</summary>
public class PlainTextChunkingTests
{
    [Fact]
    public void ShouldThrow_WhenStrategyIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => PlainTextChunking.ChunkText(null!, "text", 10));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n\t ")]
    public void ShouldReturnNoChunkWithoutCallingStrategy_WhenTextIsBlank(string? text)
    {
        var strategy = new StubChunkingStrategy();

        var chunks = strategy.ChunkText(text, 10);

        Assert.Empty(chunks);
        Assert.Empty(strategy.ChunkedDocuments);
    }

    [Fact]
    public void ShouldWrapTextInDocumentAndForwardOptions_WhenTextIsPresent()
    {
        var strategy = new StubChunkingStrategy();

        var chunks = strategy.ChunkText("abcdefghij", maxChunkSize: 4, overlap: 1, sourceId: "file-7");

        var document = Assert.Single(strategy.ChunkedDocuments);
        Assert.Equal("file-7", document.Id);
        Assert.Equal("file-7", document.SourceId);
        Assert.Equal(["abcd", "efgh", "ij"], chunks.Select(c => c.Content));
        Assert.All(chunks, c => Assert.Equal("file-7", c.SourceId));
    }

    [Fact]
    public void ShouldUseInlineSourceId_WhenNoneIsGiven()
    {
        var strategy = new StubChunkingStrategy();

        strategy.ChunkText("hello", 10);

        Assert.Equal("inline", Assert.Single(strategy.ChunkedDocuments).SourceId);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(6, 3)]
    [InlineData(1000, 4)]
    public void ShouldCountLinesBeforeOffset_WhenOffsetIsAnywhere(int offset, int expectedLine)
    {
        Assert.Equal(expectedLine, PlainTextChunking.LineNumberAt("ab\nc\nd\n", offset));
    }

    [Fact]
    public void ShouldThrow_WhenLineTextIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => PlainTextChunking.LineNumberAt(null!, 0));
    }
}
