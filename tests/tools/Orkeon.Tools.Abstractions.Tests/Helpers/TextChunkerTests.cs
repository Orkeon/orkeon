using Orkeon.Tools.Abstractions.Helpers;

namespace Orkeon.Tools.Abstractions.Tests.Helpers;

public class TextChunkerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void ChunkContent_ShouldReturnEmpty_WhenContentIsNullOrWhitespace(string content)
    {
        var result = TextChunker.ChunkContent(content, 100);

        Assert.Empty(result);
    }

    [Fact]
    public void ChunkContent_ShouldReturnSingleChunk_WhenParagraphFitsInMaxSize()
    {
        const string content = "This is a short paragraph.";

        var result = TextChunker.ChunkContent(content, 100);

        var chunk = Assert.Single(result);
        Assert.Equal("This is a short paragraph.", chunk.Text);
        Assert.Equal(1, chunk.ApproximateLine);
    }

    [Fact]
    public void ChunkContent_ShouldReturnMultipleChunks_WhenSplitByParagraphBreaks()
    {
        const string content = "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.";

        var result = TextChunker.ChunkContent(content, 100);

        Assert.Equal(3, result.Count);
        Assert.Equal("First paragraph.", result[0].Text);
        Assert.Equal("Second paragraph.", result[1].Text);
        Assert.Equal("Third paragraph.", result[2].Text);
    }

    [Fact]
    public void ChunkContent_ShouldTrackApproximateLines_WhenParagraphsSpanMultipleLines()
    {
        const string content = "Line one\nLine two\n\nNext paragraph";

        var result = TextChunker.ChunkContent(content, 100);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].ApproximateLine);
        // first paragraph spans 2 lines (+2 separator) => starts at line 5
        Assert.Equal(5, result[1].ApproximateLine);
    }

    [Fact]
    public void ChunkContent_ShouldSplitLargeParagraph_BySentenceBoundaries()
    {
        // Each sentence is ~30 chars; maxChunkSize forces a re-split.
        const string content =
            "Sentence one is here. Sentence two is here. Sentence three is here. Sentence four is here.";

        var result = TextChunker.ChunkContent(content, 40);

        Assert.True(result.Count > 1);
        Assert.All(result, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));
    }

    [Fact]
    public void ChunkContent_ShouldKeepSingleLongSentence_WhenNoSentenceBoundaryExists()
    {
        // No sentence terminator, single sentence longer than max size.
        var content = new string('a', 200);

        var result = TextChunker.ChunkContent(content, 50);

        // SplitLargeParagraph buffers the whole sentence and flushes once.
        var chunk = Assert.Single(result);
        Assert.Equal(200, chunk.Text.Length);
    }

    [Fact]
    public void ChunkContent_ShouldSplitOnQuestionAndExclamation_WhenPresent()
    {
        const string content = "Is this a question? Yes it is! And here is more text to overflow the buffer.";

        var result = TextChunker.ChunkContent(content, 25);

        Assert.True(result.Count >= 2);
    }

    [Fact]
    public void CosineSimilarity_ShouldReturnOne_WhenVectorsAreIdentical()
    {
        float[] a = [1f, 2f, 3f];
        float[] b = [1f, 2f, 3f];

        var sim = TextChunker.CosineSimilarity(a, b);

        Assert.Equal(1f, sim, 5);
    }

    [Fact]
    public void CosineSimilarity_ShouldReturnZero_WhenVectorsAreOrthogonal()
    {
        float[] a = [1f, 0f];
        float[] b = [0f, 1f];

        var sim = TextChunker.CosineSimilarity(a, b);

        Assert.Equal(0f, sim, 5);
    }

    [Fact]
    public void CosineSimilarity_ShouldReturnZero_WhenOneVectorHasZeroNorm()
    {
        float[] a = [0f, 0f, 0f];
        float[] b = [1f, 2f, 3f];

        var sim = TextChunker.CosineSimilarity(a, b);

        Assert.Equal(0f, sim);
    }

    [Fact]
    public void CosineSimilarity_ShouldThrowArgumentException_WhenLengthsDiffer()
    {
        float[] a = [1f, 2f];
        float[] b = [1f, 2f, 3f];

        Assert.Throws<ArgumentException>(() => TextChunker.CosineSimilarity(a, b));
    }
}
