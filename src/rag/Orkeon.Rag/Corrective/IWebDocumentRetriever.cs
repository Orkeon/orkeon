using Orkeon.Rag.Abstractions.Models;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Rag.Corrective;

/// <summary>
/// Web document retriever consumed by the opt-in <c>web_fallback</c> node of the
/// corrective RAG graph (RAG-06): searches the web for documents relevant to a
/// query after local retrieval and query rewriting are exhausted.
/// </summary>
/// <remarks>
/// This contract deliberately lives in <c>Orkeon.Rag</c> (not the Abstractions
/// shared kernel): it is an internal extension seam of the corrective graph, not
/// a public RAG contract. RAG-06/C3 ships an implementation plus an
/// anti-prompt-injection validator; without a registration (or with
/// <c>Corrective.WebFallback.Enabled = false</c>) the fallback edge is skipped
/// and traced.
/// </remarks>
[Experimental("ORKEXP003", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IWebDocumentRetriever
{
    /// <summary>Searches the web for up to <paramref name="maxResults"/> documents matching <paramref name="query"/>.</summary>
    Task<IReadOnlyList<RagDocument>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default);
}
