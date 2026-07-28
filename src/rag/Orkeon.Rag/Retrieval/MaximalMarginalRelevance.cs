using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Retrieval;

/// <summary>
/// Classic Maximal Marginal Relevance selection (Carbonell &amp; Goldstein 1998):
/// candidates are picked greedily, each pick maximising
/// <c>λ·relevance − (1 − λ)·max-similarity-to-already-picked</c>. <c>λ = 1</c>
/// keeps the pure relevance order; <c>λ = 0</c> maximises diversity (near-duplicates
/// are pushed back). Standalone component — wired at the fusion/dedup stage of
/// the staged pipeline (RAG-05/C2).
/// </summary>
/// <remarks>
/// <para>
/// Two honest paths, because <see cref="ScoredChunk"/> carries no embedding:
/// </para>
/// <para>
/// <b>(a) Embeddings provided</b> — classic MMR: relevance is the cosine of each
/// candidate to the query, pairwise diversity is the cosine between candidates;
/// both are mapped from <c>[-1, 1]</c> to <c>[0, 1]</c> so λ trades comparable
/// magnitudes. Use it when the caller already holds the candidate embeddings —
/// the pipeline usually does NOT (re-embedding retrieved chunks is costly).
/// </para>
/// <para>
/// <b>(b) No embeddings (realistic default)</b> — diversity is <i>approximated</i>
/// by lexical Jaccard similarity over the distinct lowercased alphanumeric tokens
/// of the chunk contents, and relevance is the min-max-normalised
/// <see cref="ScoredChunk.Score"/> (monotonic, so <c>λ = 1</c> preserves the pure
/// score order whatever the <see cref="ScoredChunk.ScoreOrigin"/> scale). This is
/// an approximation: paraphrases with disjoint vocabularies read as diverse.
/// </para>
/// <para>
/// The returned list contains the original <see cref="ScoredChunk"/> instances in
/// MMR selection order — scores and origins are never rewritten (they must survive
/// end-to-end to the citations).
/// </para>
/// </remarks>
public static class MaximalMarginalRelevance
{
    /// <summary>
    /// MMR selection with candidate embeddings (path <b>a</b>): relevance and
    /// pairwise diversity are cosine-based (mapped to <c>[0, 1]</c>).
    /// </summary>
    /// <param name="candidates">Fused candidates, typically ordered best-first (order only breaks ties).</param>
    /// <param name="queryEmbedding">Embedding of the query (relevance reference).</param>
    /// <param name="candidateEmbeddings">One embedding per candidate, same order and same dimension.</param>
    /// <param name="topK">Maximum number of selected chunks (must be positive).</param>
    /// <param name="lambda">Relevance/diversity trade-off in <c>[0, 1]</c>. Defaults to <see cref="MmrOptions.DefaultLambda"/>.</param>
    /// <returns>At most <paramref name="topK"/> original <see cref="ScoredChunk"/> instances, in MMR selection order.</returns>
    public static IReadOnlyList<ScoredChunk> Select(
        IReadOnlyList<ScoredChunk> candidates,
        ReadOnlyMemory<float> queryEmbedding,
        IReadOnlyList<ReadOnlyMemory<float>> candidateEmbeddings,
        int topK,
        double lambda = MmrOptions.DefaultLambda)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(candidateEmbeddings);
        ValidateTopKAndLambda(topK, lambda);

        if (candidateEmbeddings.Count != candidates.Count)
        {
            throw new ArgumentException(
                $"Expected one embedding per candidate ({candidates.Count}), got {candidateEmbeddings.Count}.",
                nameof(candidateEmbeddings));
        }

        if (candidates.Count == 0)
            return [];

        var relevance = new double[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
            relevance[i] = ToUnitInterval(Cosine(queryEmbedding.Span, candidateEmbeddings[i].Span));

        return SelectCore(
            candidates,
            relevance,
            (i, j) => ToUnitInterval(Cosine(candidateEmbeddings[i].Span, candidateEmbeddings[j].Span)),
            topK,
            lambda);
    }

    /// <summary>
    /// MMR selection without embeddings (path <b>b</b>, realistic default):
    /// relevance is the min-max-normalised <see cref="ScoredChunk.Score"/>,
    /// pairwise diversity is approximated by lexical Jaccard similarity over the
    /// chunk contents (documented approximation).
    /// </summary>
    /// <param name="candidates">Fused candidates, typically ordered best-first (order only breaks ties).</param>
    /// <param name="topK">Maximum number of selected chunks (must be positive).</param>
    /// <param name="lambda">Relevance/diversity trade-off in <c>[0, 1]</c>. Defaults to <see cref="MmrOptions.DefaultLambda"/>.</param>
    /// <returns>At most <paramref name="topK"/> original <see cref="ScoredChunk"/> instances, in MMR selection order.</returns>
    public static IReadOnlyList<ScoredChunk> Select(
        IReadOnlyList<ScoredChunk> candidates,
        int topK,
        double lambda = MmrOptions.DefaultLambda)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ValidateTopKAndLambda(topK, lambda);

        if (candidates.Count == 0)
            return [];

        var relevance = NormalizeScores(candidates);
        var tokens = new HashSet<string>[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
            tokens[i] = Tokenize(candidates[i].Chunk.Content);

        return SelectCore(candidates, relevance, (i, j) => Jaccard(tokens[i], tokens[j]), topK, lambda);
    }

    /// <summary>
    /// Greedy MMR loop shared by both paths. <paramref name="relevance"/> and
    /// <paramref name="similarity"/> are both in <c>[0, 1]</c> so λ trades
    /// comparable magnitudes. Ties on the MMR score (e.g. the whole first pick at
    /// <c>λ = 0</c>) fall back to the higher relevance, then to the earlier
    /// candidate (stable).
    /// </summary>
    private static List<ScoredChunk> SelectCore(
        IReadOnlyList<ScoredChunk> candidates,
        double[] relevance,
        Func<int, int, double> similarity,
        int topK,
        double lambda)
    {
        var count = candidates.Count;
        var take = Math.Min(topK, count);
        var picked = new List<int>(take);
        var isPicked = new bool[count];
        var maxSimToPicked = new double[count]; // max over an empty selection is 0 by convention

        while (picked.Count < take)
        {
            var best = -1;
            var bestMmr = double.NegativeInfinity;
            var bestRelevance = double.NegativeInfinity;

            for (var i = 0; i < count; i++)
            {
                if (isPicked[i])
                    continue;

                var mmr = (lambda * relevance[i]) - ((1 - lambda) * maxSimToPicked[i]);

                // Exact equality is intentional: it only decides ties (deterministic
                // tie-break by relevance, then by first-seen candidate).
                if (mmr > bestMmr || (mmr == bestMmr && relevance[i] > bestRelevance))
                {
                    best = i;
                    bestMmr = mmr;
                    bestRelevance = relevance[i];
                }
            }

            isPicked[best] = true;
            picked.Add(best);

            for (var i = 0; i < count; i++)
            {
                if (!isPicked[i])
                    maxSimToPicked[i] = Math.Max(maxSimToPicked[i], similarity(i, best));
            }
        }

        var selected = new List<ScoredChunk>(picked.Count);
        foreach (var index in picked)
            selected.Add(candidates[index]);
        return selected;
    }

    private static void ValidateTopKAndLambda(int topK, double lambda)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);

        if (double.IsNaN(lambda) || lambda < 0.0 || lambda > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lambda), lambda, "MMR lambda must be within [0, 1].");
        }
    }

    /// <summary>
    /// Min-max normalisation of the candidate scores to <c>[0, 1]</c> — monotonic,
    /// so λ = 1 preserves the pure score order whatever the score origin (cosine,
    /// RRF, reranker) and scale. All-equal scores normalise to 1.
    /// </summary>
    private static double[] NormalizeScores(IReadOnlyList<ScoredChunk> candidates)
    {
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
#pragma warning disable S3267 // single-pass min+max on the MMR hot path; Min()+Max() would walk the candidates twice
        foreach (var candidate in candidates)
        {
            min = Math.Min(min, candidate.Score);
            max = Math.Max(max, candidate.Score);
        }
#pragma warning restore S3267

        var relevance = new double[candidates.Count];
        var range = max - min;
        for (var i = 0; i < candidates.Count; i++)
            relevance[i] = range > 0.0 ? (candidates[i].Score - min) / range : 1.0;
        return relevance;
    }

    /// <summary>
    /// Distinct lowercased alphanumeric tokens of <paramref name="content"/> —
    /// same folding as the BM25 index (<see cref="Bm25Index.Tokenize"/>).
    /// </summary>
    private static HashSet<string> Tokenize(string content) =>
        new(Bm25Index.Tokenize(content), StringComparer.Ordinal);

    /// <summary>
    /// Jaccard similarity of two token sets in <c>[0, 1]</c>. Two empty sets are
    /// identical (1); one empty set shares nothing (0).
    /// </summary>
    private static double Jaccard(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 && right.Count == 0)
            return 1.0;
        if (left.Count == 0 || right.Count == 0)
            return 0.0;

        var (small, large) = left.Count <= right.Count ? (left, right) : (right, left);
        var intersection = small.Count(large.Contains);

        return (double)intersection / (left.Count + right.Count - intersection);
    }

    /// <summary>Cosine similarity; zero-norm vectors yield 0. Dimensions must match.</summary>
    private static double Cosine(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException(
                $"Embedding dimensions differ ({left.Length} vs {right.Length}).");
        }

        double dot = 0.0, leftNorm = 0.0, rightNorm = 0.0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += (double)left[i] * right[i];
            leftNorm += (double)left[i] * left[i];
            rightNorm += (double)right[i] * right[i];
        }

        if (leftNorm == 0.0 || rightNorm == 0.0)
            return 0.0;

        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }

    /// <summary>Maps a cosine from <c>[-1, 1]</c> to <c>[0, 1]</c> (clamped against rounding).</summary>
    private static double ToUnitInterval(double cosine) => Math.Clamp((cosine + 1.0) / 2.0, 0.0, 1.0);
}
