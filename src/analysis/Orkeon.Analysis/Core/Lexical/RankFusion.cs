namespace Orkeon.Analysis.Core.Lexical;

/// <summary>
/// Reciprocal Rank Fusion over (id, score) rankings (Cormack, Clarke &amp; Buettcher
/// 2009) — the Analysis-side implementation, written here rather than referenced because
/// the dependency must keep pointing Rag → Analysis, never back.
/// </summary>
/// <remarks>
/// <para>
/// The fused score is a RANK aggregate, not a similarity: for two lists and k = 60 it
/// lies in (0, 2/61], depends only on positions, and is not comparable with cosine or
/// BM25 values. Callers must treat it as an ordering within one fusion.
/// </para>
/// <para>
/// <b>Its k is its own, and it is fixed.</b> <see cref="DefaultK"/> is the constant from
/// the paper, which is also where the RAG pipeline's default came from — the two
/// coincide by common ancestry, not by any rule keeping them equal. The RAG side is in
/// fact operator-tunable (<c>Orkeon:Rag:Retrieval:Hybrid:RrfK</c>), so the values already
/// differ on any deployment that sets that key; code search deliberately exposes no such
/// knob. Do not "resynchronize" them (ADR-009).
/// </para>
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
