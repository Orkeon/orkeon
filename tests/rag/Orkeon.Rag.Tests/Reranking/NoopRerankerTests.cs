using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Reranking;

namespace Orkeon.Rag.Tests.Reranking;

public class NoopRerankerTests
{
    private static ScoredChunk MakeChunk(int index, double score) => new()
    {
        Chunk = new Chunk
        {
            Id = $"chunk-{index}",
            DocumentId = "doc",
            SourceId = "source",
            Content = $"content {index}",
            Index = index,
        },
        Score = score,
        ScoreOrigin = "vector",
    };

    [Fact]
    public void Name_IsNone()
    {
        Assert.Equal("none", new NoopReranker().Name);
        Assert.Equal(NoopReranker.RerankerName, new NoopReranker().Name);
    }

    [Fact]
    public async Task Rerank_PreservesOrderAndScores_TruncatedToTopN()
    {
        var candidates = new[] { MakeChunk(0, 0.9), MakeChunk(1, 0.8), MakeChunk(2, 0.7) };

        var result = await new NoopReranker().RerankAsync("query", candidates, topN: 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Same(candidates[0], result[0]);
        Assert.Same(candidates[1], result[1]);
        Assert.Equal("vector", result[0].ScoreOrigin);
    }

    [Fact]
    public async Task Rerank_TopNLargerThanCandidates_ReturnsAll()
    {
        var candidates = new[] { MakeChunk(0, 0.9), MakeChunk(1, 0.8) };

        var result = await new NoopReranker().RerankAsync("query", candidates, topN: 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Rerank_TopNZeroOrNegative_ReturnsEmpty()
    {
        var candidates = new[] { MakeChunk(0, 0.9) };

        Assert.Empty(await new NoopReranker().RerankAsync("query", candidates, topN: 0, TestContext.Current.CancellationToken));
        Assert.Empty(await new NoopReranker().RerankAsync("query", candidates, topN: -1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rerank_EmptyCandidates_ReturnsEmpty()
    {
        Assert.Empty(await new NoopReranker().RerankAsync("query", [], topN: 5, TestContext.Current.CancellationToken));
    }
}
