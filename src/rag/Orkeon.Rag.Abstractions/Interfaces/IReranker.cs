using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Reorders retrieval candidates by fine-grained relevance and truncates to the
/// best <c>topN</c> (50 → 5 cascade, guide §7). Named rerankers are resolved by
/// the reranker factory in <c>Orkeon.Rag</c>.
/// </summary>
public interface IReranker
{
    /// <summary>Reranker name used for factory resolution (e.g. <c>onnx</c>, <c>llm</c>, <c>none</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Returns at most <paramref name="topN"/> chunks from <paramref name="candidates"/>,
    /// best first, with scores replaced by the reranker's own scores.
    /// </summary>
    Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topN,
        CancellationToken cancellationToken = default);
}
