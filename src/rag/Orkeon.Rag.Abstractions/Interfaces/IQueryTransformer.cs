using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Produces retrieval-friendly variants of a user query (Multi-Query, RAG-Fusion,
/// HyDE — guide §6). Named transformers are resolved by the transformer factory
/// in <c>Orkeon.Rag</c>.
/// </summary>
public interface IQueryTransformer
{
    /// <summary>Transformer name used for factory resolution (e.g. <c>multi-query</c>, <c>hyde</c>).</summary>
    string Name { get; }

    /// <summary>
    /// How the retrieve stage must combine the result lists retrieved for the
    /// returned queries: <see cref="QueryTransformKind.Union"/> (merge + dedup,
    /// the default), <see cref="QueryTransformKind.Fusion"/> (Reciprocal Rank
    /// Fusion across the per-query rankings), or
    /// <see cref="QueryTransformKind.Replacement"/> (the returned text is
    /// embedded instead of the original question — HyDE).
    /// </summary>
    QueryTransformKind Kind => QueryTransformKind.Union;

    /// <summary>
    /// Returns the ordered list of retrieval texts for <paramref name="query"/>.
    /// <see cref="QueryTransformKind.Union"/>/<see cref="QueryTransformKind.Fusion"/>
    /// transformers include the original query as the FIRST element, followed by
    /// the variants; <see cref="QueryTransformKind.Replacement"/> transformers
    /// return substitute text(s) only (the original query is deliberately absent).
    /// An empty list means "retrieve with the original only". Implementations
    /// never throw on an unusable LLM response — they fall back to
    /// <c>[query]</c> with a logged warning.
    /// </summary>
    Task<IReadOnlyList<string>> TransformAsync(
        string query,
        QueryTransformContext context,
        CancellationToken cancellationToken = default);
}
