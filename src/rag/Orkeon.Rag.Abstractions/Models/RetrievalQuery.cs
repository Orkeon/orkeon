using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// A single retrieval request against an <see cref="Interfaces.IDocumentStore"/> collection.
/// </summary>
public sealed record RetrievalQuery
{
    /// <summary>Query text (used for full-text/hybrid search and by rerankers).</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Pre-computed embedding of <see cref="Text"/>. When <c>null</c>, stores
    /// without server-side embedding fall back to text-only search.
    /// </summary>
    public ImmutableArray<float>? Embedding { get; init; }

    /// <summary>
    /// Number of candidates to retrieve. Defaults to the 50-candidate stage of the
    /// 50 → 5 rerank cascade (guide §7.3).
    /// </summary>
    public int TopK { get; init; } = 50;

    /// <summary>Optional metadata filters (key must equal value).</summary>
    public ImmutableDictionary<string, string> Filters { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
