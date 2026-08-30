using Orkeon.Domain.Constants.Rag;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Retrieval;

/// <summary>
/// Reciprocal Rank Fusion (RRF) of independently ranked candidate lists
/// (Cormack, Clarke &amp; Buettcher 2009): each list contributes
/// <c>1 / (k + rank)</c> per candidate (rank is 1-based), contributions are summed by
/// chunk id, and candidates are re-ranked by the fused score.
/// </summary>
/// <remarks>
/// <para>
/// <b>The RRF score is NOT a similarity.</b> It is a pure rank aggregate: for two lists
/// and the standard <c>k = 60</c> it lies in <c>(0, 2/61]</c>, depends only on the
/// candidates' positions (never on the input scores), and is not comparable with cosine
/// similarities, BM25 scores, or RRF scores computed with another <c>k</c> or another
/// number of lists. Fused results therefore carry
/// <see cref="ScoredChunk.ScoreOrigin"/> = <c>rrf</c>; use them for ordering and
/// cut-offs within one fusion only.
/// </para>
/// <para>
/// Deduplication is by <see cref="Chunk.Id"/>; the <see cref="Chunk"/> payload of the
/// first list containing the id wins (payloads are expected identical across lists).
/// </para>
/// </remarks>
public static class ReciprocalRankFusion
{
    /// <summary><see cref="ScoredChunk.ScoreOrigin"/> of fused scores.</summary>
    public const string RrfScoreOrigin = "rrf";

    /// <summary>
    /// The standard RRF constant (Cormack et al. 2009). Declared once, in
    /// <see cref="RagDefaults.RrfK"/>: the same section
    /// (<c>Orkeon:Rag:Retrieval:Hybrid</c>) is bound by two option classes in two
    /// projects — <see cref="HybridRetrievalOptions"/> here and <c>RagHybridOptions</c>
    /// in Orkeon.Rag.Abstractions — and each used to carry its own copy of 60, so
    /// changing the product default would have moved one fusion and not the other.
    /// </summary>
    public const int DefaultK = RagDefaults.RrfK;

    /// <summary>
    /// Fuses ranked candidate lists into a single ranking of at most
    /// <paramref name="topK"/> chunks, best fused score first.
    /// </summary>
    /// <param name="k">The RRF constant (must be positive; 60 is the standard).</param>
    /// <param name="topK">Maximum number of fused results (must be positive).</param>
    /// <param name="rankings">
    /// The ranked lists, each ordered best-first. Empty lists contribute nothing.
    /// </param>
    /// <returns>The fused ranking with <see cref="ScoredChunk.ScoreOrigin"/> = <c>rrf</c>.</returns>
    public static IReadOnlyList<ScoredChunk> Fuse(
        int k,
        int topK,
        params IReadOnlyList<ScoredChunk>[] rankings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        ArgumentNullException.ThrowIfNull(rankings);

        var fused = new Dictionary<string, (Chunk Chunk, double Score)>(StringComparer.Ordinal);

        foreach (var ranking in rankings)
        {
            ArgumentNullException.ThrowIfNull(ranking);

            for (var rank = 1; rank <= ranking.Count; rank++)
            {
                var candidate = ranking[rank - 1];
                var contribution = 1.0 / (k + rank);

                fused[candidate.Chunk.Id] = fused.TryGetValue(candidate.Chunk.Id, out var existing)
                    ? (existing.Chunk, existing.Score + contribution)
                    : (candidate.Chunk, contribution);
            }
        }

        return fused
            .OrderByDescending(pair => pair.Value.Score)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(topK)
            .Select(pair => new ScoredChunk
            {
                Chunk = pair.Value.Chunk,
                Score = pair.Value.Score,
                ScoreOrigin = RrfScoreOrigin,
            })
            .ToList();
    }
}
