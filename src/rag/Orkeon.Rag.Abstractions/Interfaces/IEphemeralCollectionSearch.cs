using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Semantic search over an auto-ingested <b>ephemeral collection</b> (RAG-03/C5):
/// given a set of sources and a query, ingests the sources incrementally into a
/// deterministic collection (unchanged corpus = zero embeddings, per the
/// collection manifest) and returns the scored candidates. This is the shared
/// engine behind the thin search-tool facades (<c>txt_search</c>,
/// <c>mdx_search</c>, <c>pdf_search</c>, <c>directory_search</c>), which used to
/// re-embed their whole corpus on every request.
/// </summary>
public interface IEphemeralCollectionSearch
{
    /// <summary>Ingests <see cref="EphemeralSearchRequest.Sources"/> (incrementally) then searches them.</summary>
    Task<EphemeralSearchResult> SearchAsync(
        EphemeralSearchRequest request,
        CancellationToken cancellationToken = default);
}
