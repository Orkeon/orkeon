namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// A retrieved <see cref="Models.Chunk"/> with its relevance score. Scores must
/// survive end-to-end from the store to the final <see cref="RagAnswer"/> citations.
/// </summary>
public sealed record ScoredChunk
{
    /// <summary>The retrieved chunk.</summary>
    public required Chunk Chunk { get; init; }

    /// <summary>
    /// Relevance score, higher is better. Typically a cosine similarity in [0, 1],
    /// an RRF fusion score, or a reranker score — see <see cref="ScoreOrigin"/>.
    /// </summary>
    public required double Score { get; init; }

    /// <summary>
    /// Provenance of <see cref="Score"/> (e.g. <c>vector</c>, <c>bm25</c>, <c>rrf</c>,
    /// <c>reranker</c>). Scores from different origins are not directly comparable.
    /// </summary>
    public string? ScoreOrigin { get; init; }
}
