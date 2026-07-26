using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Retrieval;

namespace Orkeon.Rag.Tests.Retrieval;

/// <summary>
/// Tests for <see cref="MaximalMarginalRelevance"/> (RAG-05/C2): hand-built cases
/// for both honest paths — embeddings (classic cosine MMR) and the no-embeddings
/// lexical Jaccard approximation.
/// </summary>
public class MaximalMarginalRelevanceTests
{
    private static ScoredChunk Scored(string id, string content, double score) =>
        RetrievalTestData.MakeScored(RetrievalTestData.MakeChunk(id, content), score);

    private static string[] Ids(IReadOnlyList<ScoredChunk> selection) =>
        selection.Select(s => s.Chunk.Id).ToArray();

    // ── Jaccard path (no embeddings — realistic default) ──────────────────

    [Fact]
    public void Jaccard_LambdaOne_OrdersByPureScore()
    {
        // Duplicate contents on purpose: λ = 1 must ignore diversity entirely.
        var candidates = new[]
        {
            Scored("c-low", "same exact tokens everywhere", 0.5),
            Scored("c-top", "same exact tokens everywhere", 0.9),
            Scored("c-mid", "same exact tokens everywhere", 0.7),
        };

        var selection = MaximalMarginalRelevance.Select(candidates, topK: 3, lambda: 1.0);

        Assert.Equal(["c-top", "c-mid", "c-low"], Ids(selection));
    }

    [Fact]
    public void Jaccard_LambdaZero_PushesNearDuplicateBack()
    {
        var candidates = new[]
        {
            Scored("a", "the quick brown fox jumps", 0.95),
            Scored("a-dup", "the quick brown fox leaps", 0.94), // near-duplicate of "a"
            Scored("b", "sunset over mountain lake", 0.50),
        };

        var selection = MaximalMarginalRelevance.Select(candidates, topK: 3, lambda: 0.0);

        // First pick ties at 0 → highest relevance wins; then the lexically
        // distant chunk beats the near-duplicate.
        Assert.Equal(["a", "b", "a-dup"], Ids(selection));
    }

    [Fact]
    public void Jaccard_TwoContentClusters_AlternatesSelection()
    {
        var candidates = new[]
        {
            Scored("x1", "apple banana cherry", 1.0),
            Scored("x2", "apple banana cherry date", 0.9),
            Scored("y1", "wolf tiger lion", 0.8),
            Scored("y2", "wolf tiger lion bear", 0.7),
        };

        var selection = MaximalMarginalRelevance.Select(candidates, topK: 4, lambda: 0.5);

        Assert.Equal(["x1", "y1", "x2", "y2"], Ids(selection));
    }

    [Fact]
    public void Jaccard_TopKSmallerThanCandidates_Truncates()
    {
        var candidates = new[]
        {
            Scored("x1", "apple banana cherry", 1.0),
            Scored("x2", "apple banana cherry date", 0.9),
            Scored("y1", "wolf tiger lion", 0.8),
            Scored("y2", "wolf tiger lion bear", 0.7),
        };

        var selection = MaximalMarginalRelevance.Select(candidates, topK: 2, lambda: 0.5);

        Assert.Equal(["x1", "y1"], Ids(selection));
    }

    [Fact]
    public void Jaccard_TopKLargerThanCandidates_ReturnsAll()
    {
        var candidates = new[]
        {
            Scored("a", "alpha beta", 0.9),
            Scored("b", "gamma delta", 0.8),
        };

        var selection = MaximalMarginalRelevance.Select(candidates, topK: 10, lambda: 0.7);

        Assert.Equal(2, selection.Count);
    }

    [Fact]
    public void Jaccard_EmptyCandidates_ReturnsEmpty()
    {
        var selection = MaximalMarginalRelevance.Select(Array.Empty<ScoredChunk>(), topK: 5, lambda: 0.7);

        Assert.Empty(selection);
    }

    [Fact]
    public void Jaccard_PreservesOriginalScoresAndOrigins()
    {
        var candidates = new[]
        {
            Scored("a", "apple banana cherry", 0.032), // e.g. an RRF-scale score
            Scored("b", "wolf tiger lion", 0.016),
        };

        var selection = MaximalMarginalRelevance.Select(candidates, topK: 2, lambda: 0.5);

        // MMR selects and orders — it never rewrites scores.
        Assert.Same(candidates[0], selection[0]);
        Assert.Same(candidates[1], selection[1]);
        Assert.Equal(0.032, selection[0].Score);
        Assert.Equal("vector", selection[0].ScoreOrigin);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Jaccard_LambdaOutOfRange_Throws(double lambda)
    {
        var candidates = new[] { Scored("a", "alpha", 0.9) };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => MaximalMarginalRelevance.Select(candidates, topK: 1, lambda: lambda));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Jaccard_NonPositiveTopK_Throws(int topK)
    {
        var candidates = new[] { Scored("a", "alpha", 0.9) };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => MaximalMarginalRelevance.Select(candidates, topK: topK, lambda: 0.7));
    }

    // ── Embeddings path (classic cosine MMR) ──────────────────────────────

    private static ReadOnlyMemory<float>[] Embeddings(params float[][] vectors) =>
        vectors.Select(v => new ReadOnlyMemory<float>(v)).ToArray();

    [Fact]
    public void Embeddings_LambdaOne_OrdersByQueryCosine()
    {
        // Store scores deliberately contradict the embeddings: λ = 1 must order
        // by cosine to the query (classic MMR relevance), not by ScoredChunk.Score.
        var candidates = new[]
        {
            Scored("far", "far", 0.9),
            Scored("near", "near", 0.1),
            Scored("mid", "mid", 0.5),
        };
        var query = new ReadOnlyMemory<float>([1f, 0f]);
        var embeddings = Embeddings(
            [0.2f, 0.98f],  // far:  cos ≈ 0.20
            [1f, 0f],       // near: cos = 1
            [0.6f, 0.8f]);  // mid:  cos = 0.60

        var selection = MaximalMarginalRelevance.Select(candidates, query, embeddings, topK: 3, lambda: 1.0);

        Assert.Equal(["near", "mid", "far"], Ids(selection));
    }

    [Fact]
    public void Embeddings_LambdaZero_PushesNearIdenticalEmbeddingBack()
    {
        var candidates = new[]
        {
            Scored("a", "a", 0.95),
            Scored("a-dup", "a-dup", 0.94),
            Scored("b", "b", 0.50),
        };
        var query = new ReadOnlyMemory<float>([1f, 0f]);
        var embeddings = Embeddings(
            [1f, 0f],           // a: aligned with the query
            [0.999f, 0.0447f],  // a-dup: near-identical to a
            [0f, 1f]);          // b: orthogonal

        var selection = MaximalMarginalRelevance.Select(candidates, query, embeddings, topK: 3, lambda: 0.0);

        Assert.Equal(["a", "b", "a-dup"], Ids(selection));
    }

    [Fact]
    public void Embeddings_TwoClusters_AlternatesSelection()
    {
        var candidates = new[]
        {
            Scored("e1", "e1", 1.0),
            Scored("e2", "e2", 0.9),
            Scored("f1", "f1", 0.8),
            Scored("f2", "f2", 0.7),
        };
        // Two orthogonal 4-D clusters; the query leans towards cluster E.
        var query = new ReadOnlyMemory<float>([1f, 0f, 0.5f, 0f]);
        var embeddings = Embeddings(
            [1f, 0f, 0f, 0f],     // cluster E
            [1f, 0.2f, 0f, 0f],   // cluster E (close to e1)
            [0f, 0f, 1f, 0f],     // cluster F
            [0f, 0f, 1f, 0.2f]);  // cluster F (close to f1)

        var selection = MaximalMarginalRelevance.Select(candidates, query, embeddings, topK: 4, lambda: 0.5);

        Assert.Equal(["e1", "f1", "e2", "f2"], Ids(selection));
    }

    [Fact]
    public void Embeddings_TopK_Truncates()
    {
        var candidates = new[]
        {
            Scored("a", "a", 0.9),
            Scored("b", "b", 0.8),
            Scored("c", "c", 0.7),
        };
        var query = new ReadOnlyMemory<float>([1f, 0f]);
        var embeddings = Embeddings([1f, 0f], [0.8f, 0.6f], [0f, 1f]);

        var selection = MaximalMarginalRelevance.Select(candidates, query, embeddings, topK: 2, lambda: 0.7);

        Assert.Equal(2, selection.Count);
        Assert.Equal("a", selection[0].Chunk.Id);
    }

    [Fact]
    public void Embeddings_EmptyCandidates_ReturnsEmpty()
    {
        var selection = MaximalMarginalRelevance.Select(
            Array.Empty<ScoredChunk>(),
            new ReadOnlyMemory<float>([1f, 0f]),
            Array.Empty<ReadOnlyMemory<float>>(),
            topK: 5,
            lambda: 0.7);

        Assert.Empty(selection);
    }

    [Fact]
    public void Embeddings_CountMismatch_Throws()
    {
        var candidates = new[] { Scored("a", "a", 0.9), Scored("b", "b", 0.8) };
        var query = new ReadOnlyMemory<float>([1f, 0f]);
        var embeddings = Embeddings([1f, 0f]); // one embedding for two candidates

        var exception = Assert.Throws<ArgumentException>(
            () => MaximalMarginalRelevance.Select(candidates, query, embeddings, topK: 2, lambda: 0.7));
        Assert.Equal("candidateEmbeddings", exception.ParamName);
    }

    [Fact]
    public void Embeddings_DimensionMismatch_Throws()
    {
        var candidates = new[] { Scored("a", "a", 0.9) };
        var query = new ReadOnlyMemory<float>([1f, 0f, 0f]);
        var embeddings = Embeddings([1f, 0f]); // 2-D vs 3-D query

        Assert.Throws<ArgumentException>(
            () => MaximalMarginalRelevance.Select(candidates, query, embeddings, topK: 1, lambda: 0.7));
    }
}
