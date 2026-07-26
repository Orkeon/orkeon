using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Reranking;

/// <summary>
/// Identity reranker: keeps the candidates in their incoming order, truncated to
/// <c>topN</c>, scores untouched. Used by the <c>Fast</c> profile and as the
/// explicit "no reranking" choice (<c>none</c>/<c>noop</c>).
/// </summary>
public sealed class NoopReranker : IReranker
{
    /// <summary>Canonical factory name (<c>noop</c> is the registered alias).</summary>
    public const string RerankerName = "none";

    /// <inheritdoc />
    public string Name => RerankerName;

    /// <inheritdoc />
    public Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topN,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidates);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ScoredChunk> result = topN >= candidates.Count
            ? candidates
            : [.. candidates.Take(Math.Max(0, topN))];

        return Task.FromResult(result);
    }
}
