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
    /// Returns query variants for <paramref name="query"/> (the original query is
    /// not included; an empty list means "retrieve with the original only").
    /// </summary>
    Task<IReadOnlyList<string>> TransformAsync(
        string query,
        QueryTransformContext context,
        CancellationToken cancellationToken = default);
}
