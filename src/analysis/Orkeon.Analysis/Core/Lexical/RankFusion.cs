namespace Orkeon.Analysis.Core.Lexical;

/// <summary>
/// Reciprocal Rank Fusion over (id, score) rankings — the Analysis-side twin of
/// <c>Orkeon.Rag.Retrieval.ReciprocalRankFusion</c> (Cormack, Clarke &amp; Buettcher
/// 2009), duplicated rather than referenced because the dependency must keep pointing
/// Rag → Analysis, never back. Same k, same semantics.
/// </summary>
/// <remarks>
/// The fused score is a RANK aggregate, not a similarity: for two lists and k = 60 it
/// lies in (0, 2/61], depends only on positions, and is not comparable with cosine or
/// BM25 values. Callers must treat it as an ordering within one fusion.
/// </remarks>
public static class RankFusion
{
    /// <summary>The standard RRF constant (Cormack et al. 2009).</summary>
    public const int DefaultK = 60;

    /// <summary>
    /// Fuses best-first rankings into at most <paramref name="topK"/> (id, fusedScore,
    /// origins) entries, best fused score first. <c>origins</c> is the bitwise union of
    /// each list's index (bit i set ⇔ present in rankings[i]) so the caller can label a
    /// hit "vector", "bm25" or "hybrid".
    /// </summary>
    public static IReadOnlyList<(string Id, double Score, int Origins)> Fuse(
        int topK,
        params IReadOnlyList<(string Id, double Score)>[] rankings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        ArgumentNullException.ThrowIfNull(rankings);

        var fused = new Dictionary<string, (double Score, int Origins)>(StringComparer.Ordinal);
        for (var list = 0; list < rankings.Length; list++)
        {
            var ranking = rankings[list];
            ArgumentNullException.ThrowIfNull(ranking);
            for (var rank = 1; rank <= ranking.Count; rank++)
            {
                var id = ranking[rank - 1].Id;
                var contribution = 1.0 / (DefaultK + rank);
                fused[id] = fused.TryGetValue(id, out var existing)
                    ? (existing.Score + contribution, existing.Origins | (1 << list))
                    : (contribution, 1 << list);
            }
        }

        return [.. fused
            .OrderByDescending(pair => pair.Value.Score)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(topK)
            .Select(pair => (pair.Key, pair.Value.Score, pair.Value.Origins))];
    }
}
