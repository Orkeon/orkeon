using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Corrective;

namespace Orkeon.Rag.WebFallback;

/// <summary>
/// Bridges the secure web search transport (lot 6C,
/// <see cref="WebSearchDocumentRetriever"/>) onto the corrective graph's
/// <see cref="IWebDocumentRetriever"/> seam (lot 6A) by plain delegation
/// (RAG-06/6D). Registered by <c>AddOrkeonRagWebFallback</c> ONLY when the
/// transport is actually usable (<c>Orkeon:Rag:WebFallback:Enabled</c> set and
/// an endpoint configured) — otherwise no <see cref="IWebDocumentRetriever"/>
/// exists and the graph's <c>web_fallback</c> edge is skipped and traced.
/// </summary>
public sealed class WebSearchDocumentRetrieverAdapter : IWebDocumentRetriever
{
    private readonly WebSearchDocumentRetriever _inner;

    /// <summary>Creates the adapter over the concrete retriever.</summary>
    public WebSearchDocumentRetrieverAdapter(WebSearchDocumentRetriever inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RagDocument>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default)
        => _inner.SearchAsync(query, maxResults, cancellationToken);
}
