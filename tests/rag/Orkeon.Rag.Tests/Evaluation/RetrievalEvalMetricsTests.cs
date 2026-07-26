using Orkeon.Rag.Evaluation;

namespace Orkeon.Rag.Tests.Evaluation;

/// <summary>
/// Hand-computed synthetic cases for the deterministic retrieval metrics
/// (recall@k, precision@k, reciprocal rank) and the ref-matching semantics.
/// </summary>
public sealed class RetrievalEvalMetricsTests
{
    // Ranked retrieved items: each entry is the identifier set one item exposes.
    private static readonly string[][] Ranked =
    [
        ["chunk-a#0", "doc-a", "/workspace/corpus/doc-a.md"], // rank 1 → doc-a
        ["chunk-x#0", "doc-x", "/workspace/corpus/doc-x.md"], // rank 2 → noise
        ["chunk-b#0", "doc-b", "/workspace/corpus/doc-b.md"], // rank 3 → doc-b
        ["chunk-y#0", "doc-y", "/workspace/corpus/doc-y.md"], // rank 4 → noise
        ["chunk-z#0", "doc-z", "/workspace/corpus/doc-z.md"], // rank 5 → noise
        ["chunk-c#0", "doc-c", "/workspace/corpus/doc-c.md"], // rank 6 → doc-c (beyond k=5)
    ];

    private static readonly string[] Relevant = ["corpus/doc-a.md", "corpus/doc-b.md", "corpus/doc-c.md"];

    [Fact]
    public void RecallAtK_CountsRelevantRefsFoundInTheTopK()
    {
        // Hand-computed: doc-a (rank 1) and doc-b (rank 3) are inside the top 5,
        // doc-c only appears at rank 6 → 2 found / 3 relevant.
        Assert.Equal(2.0 / 3.0, RetrievalEvalMetrics.RecallAtK(Relevant, Ranked, 5), precision: 10);

        // k=6 reaches doc-c → 3/3.
        Assert.Equal(1.0, RetrievalEvalMetrics.RecallAtK(Relevant, Ranked, 6), precision: 10);

        // k=1 only sees doc-a → 1/3.
        Assert.Equal(1.0 / 3.0, RetrievalEvalMetrics.RecallAtK(Relevant, Ranked, 1), precision: 10);
    }

    [Fact]
    public void PrecisionAtK_UsesKAsDenominator_MissingRanksCountAsMisses()
    {
        // Hand-computed: relevant hits in the first 5 slots are ranks 1 and 3 → 2/5.
        Assert.Equal(2.0 / 5.0, RetrievalEvalMetrics.PrecisionAtK(Relevant, Ranked, 5), precision: 10);

        // Only 6 items retrieved but k=10: 3 hits / 10 slots.
        Assert.Equal(3.0 / 10.0, RetrievalEvalMetrics.PrecisionAtK(Relevant, Ranked, 10), precision: 10);
    }

    [Fact]
    public void ReciprocalRank_IsOneOverTheFirstMatchingRank()
    {
        Assert.Equal(1.0, RetrievalEvalMetrics.ReciprocalRank(Relevant, Ranked), precision: 10);

        // Only doc-c is relevant → first match at rank 6 → 1/6.
        Assert.Equal(1.0 / 6.0, RetrievalEvalMetrics.ReciprocalRank(["corpus/doc-c.md"], Ranked), precision: 10);

        // Nothing matches → 0.
        Assert.Equal(0.0, RetrievalEvalMetrics.ReciprocalRank(["corpus/absent.md"], Ranked), precision: 10);
    }

    [Fact]
    public void Metrics_WithNoRetrievedItem_AreZero()
    {
        var empty = Array.Empty<IReadOnlyList<string>>();
        Assert.Equal(0.0, RetrievalEvalMetrics.RecallAtK(Relevant, empty, 5));
        Assert.Equal(0.0, RetrievalEvalMetrics.PrecisionAtK(Relevant, empty, 5));
        Assert.Equal(0.0, RetrievalEvalMetrics.ReciprocalRank(Relevant, empty));
    }

    [Fact]
    public void Guards_RejectEmptyRelevant_AndNonPositiveK()
    {
        Assert.Throws<ArgumentException>(() => RetrievalEvalMetrics.RecallAtK([], Ranked, 5));
        Assert.Throws<ArgumentException>(() => RetrievalEvalMetrics.ReciprocalRank([], Ranked));
        Assert.Throws<ArgumentOutOfRangeException>(() => RetrievalEvalMetrics.PrecisionAtK(Relevant, Ranked, 0));
    }

    [Theory]
    // Exact identifier matches (chunk/document ids).
    [InlineData("chunk-a#0", "chunk-a#0", true)]
    [InlineData("doc-a", "doc-a", true)]
    // Path-suffix matches, fragment stripped, './' ignored, case-insensitive, backslashes normalized.
    [InlineData("/workspace/examples/rag/eval/corpus/faq.md", "corpus/faq.md", true)]
    [InlineData("/workspace/examples/rag/eval/corpus/faq.md", "corpus/faq.md#refunds", true)]
    [InlineData("/workspace/examples/rag/eval/corpus/faq.md", "./corpus/faq.md", true)]
    [InlineData("/workspace/CORPUS/FAQ.MD", "corpus/faq.md", true)]
    [InlineData("C:\\data\\corpus\\faq.md", "corpus/faq.md", true)]
    [InlineData("/workspace/corpus/faq.md", "faq.md", true)]
    // Non-matches: different directory suffix, partial file name, empty.
    [InlineData("/workspace/corpus/faq.md", "notes/faq.md", false)]
    [InlineData("/workspace/corpus/otherfaq.md", "faq.md", false)]
    [InlineData("", "faq.md", false)]
    [InlineData("/workspace/corpus/faq.md", "", false)]
    public void Matches_AppliesExactAndPathSuffixSemantics(string identifier, string reference, bool expected)
        => Assert.Equal(expected, RetrievalEvalMetrics.Matches(identifier, reference));
}
