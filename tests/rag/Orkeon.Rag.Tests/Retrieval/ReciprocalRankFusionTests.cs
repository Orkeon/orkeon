using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Retrieval;
using static Orkeon.Rag.Tests.Retrieval.RetrievalTestData;

namespace Orkeon.Rag.Tests.Retrieval;

/// <summary>
/// Tests of Reciprocal Rank Fusion (RAG-04/C2) against hand-computed expectations:
/// with k = 60 each list contributes 1/(60 + rank), summed per chunk id.
/// </summary>
public class ReciprocalRankFusionTests
{
    private static readonly Chunk A = MakeChunk("A", "alpha");
    private static readonly Chunk B = MakeChunk("B", "beta");
    private static readonly Chunk C = MakeChunk("C", "gamma");
    private static readonly Chunk D = MakeChunk("D", "delta");

    [Fact]
    public void Fuse_TwoLists_HandComputedOrderAndScores()
    {
        // List 1: [A, B, C] ; List 2: [B, D].
        // A = 1/61 ≈ 0.016393 ; B = 1/62 + 1/61 ≈ 0.032522 ;
        // C = 1/63 ≈ 0.015873 ; D = 1/62 ≈ 0.016129.  Expected order: B, A, D, C.
        IReadOnlyList<ScoredChunk> vector = [MakeScored(A, 0.9), MakeScored(B, 0.8), MakeScored(C, 0.7)];
        IReadOnlyList<ScoredChunk> lexical = [MakeScored(B, 12.0, "bm25"), MakeScored(D, 3.0, "bm25")];

        var fused = ReciprocalRankFusion.Fuse(60, topK: 10, vector, lexical);

        Assert.Equal(["B", "A", "D", "C"], fused.Select(r => r.Chunk.Id));
        Assert.Equal((1.0 / 62) + (1.0 / 61), fused[0].Score, precision: 10);
        Assert.Equal(1.0 / 61, fused[1].Score, precision: 10);
        Assert.Equal(1.0 / 62, fused[2].Score, precision: 10);
        Assert.Equal(1.0 / 63, fused[3].Score, precision: 10);
        Assert.All(fused, r => Assert.Equal(ReciprocalRankFusion.RrfScoreOrigin, r.ScoreOrigin));
    }

    [Fact]
    public void Fuse_ScoresDependOnlyOnRanks_NeverOnInputScores()
    {
        IReadOnlyList<ScoredChunk> tiny = [MakeScored(A, 0.0001), MakeScored(B, 0.00005)];
        IReadOnlyList<ScoredChunk> huge = [MakeScored(A, 9_999.0, "bm25"), MakeScored(B, 5_000.0, "bm25")];

        var fused = ReciprocalRankFusion.Fuse(60, topK: 10, tiny, huge);

        // Same ranks in both lists → identical contributions regardless of magnitudes.
        Assert.Equal(2.0 / 61, fused[0].Score, precision: 10);
        Assert.Equal(2.0 / 62, fused[1].Score, precision: 10);
    }

    [Fact]
    public void Fuse_TruncatesToTopK()
    {
        IReadOnlyList<ScoredChunk> vector = [MakeScored(A, 0.9), MakeScored(B, 0.8), MakeScored(C, 0.7)];
        IReadOnlyList<ScoredChunk> lexical = [MakeScored(B, 1.0, "bm25")];

        var fused = ReciprocalRankFusion.Fuse(60, topK: 2, vector, lexical);

        Assert.Equal(["B", "A"], fused.Select(r => r.Chunk.Id));
    }

    [Fact]
    public void Fuse_DeduplicatesByChunkId_FirstPayloadWins()
    {
        var duplicate = MakeChunk("A", "same id, other payload");
        IReadOnlyList<ScoredChunk> first = [MakeScored(A, 0.9)];
        IReadOnlyList<ScoredChunk> second = [MakeScored(duplicate, 5.0, "bm25")];

        var fused = ReciprocalRankFusion.Fuse(60, topK: 10, first, second);

        var single = Assert.Single(fused);
        Assert.Same(A, single.Chunk);
    }

    [Fact]
    public void Fuse_CustomK_ChangesTheContributions()
    {
        IReadOnlyList<ScoredChunk> first = [MakeScored(A, 0.9), MakeScored(B, 0.8)];
        IReadOnlyList<ScoredChunk> second = [MakeScored(B, 1.0, "bm25")];

        var fused = ReciprocalRankFusion.Fuse(1, topK: 10, first, second);

        // B = 1/(1+2) + 1/(1+1) = 5/6 ; A = 1/(1+1) = 1/2.
        Assert.Equal("B", fused[0].Chunk.Id);
        Assert.Equal((1.0 / 3) + (1.0 / 2), fused[0].Score, precision: 10);
        Assert.Equal(1.0 / 2, fused[1].Score, precision: 10);
    }

    [Fact]
    public void Fuse_EmptyLists_YieldEmpty()
    {
        Assert.Empty(ReciprocalRankFusion.Fuse(60, topK: 5));
        Assert.Empty(ReciprocalRankFusion.Fuse(60, topK: 5, [], []));
    }

    [Fact]
    public void Fuse_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReciprocalRankFusion.Fuse(0, topK: 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReciprocalRankFusion.Fuse(60, topK: 0));
        Assert.Throws<ArgumentNullException>(() => ReciprocalRankFusion.Fuse(60, topK: 5, rankings: null!));
    }
}
