using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Search;

namespace Orkeon.Rag.DependencyInjection;

/// <summary>
/// Registration of the ephemeral-collection search engine (RAG-03/C5) consumed
/// by the thin search-tool facades (<c>txt_search</c>, <c>mdx_search</c>,
/// <c>pdf_search</c>, <c>directory_search</c>). Safe to call from any tool
/// package (<c>TryAdd*</c> everywhere, idempotent).
/// </summary>
/// <remarks>
/// The engine rides on the RAG subsystem: <c>AddOrkeonRag(configuration)</c>
/// must be registered (plus an <see cref="IEmbeddingProvider"/>, which
/// <c>AddOrkeonInfrastructure()</c> provides) for searches to run. When the
/// subsystem is absent, tool <b>registration and construction still succeed</b>
/// — the search facades only fail at call time, loudly and with an actionable
/// message (same lazy-failure philosophy as the former no-op embedding fallback).
/// </remarks>
public static class EphemeralSearchExtensions
{
    /// <summary>
    /// Registers <see cref="IEphemeralCollectionSearch"/> and the inline-text
    /// document loader it relies on.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrkeonEphemeralSearch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Inline text sources ("kind": "text") used by the facades to submit
        // preprocessed content (stripped frontmatter, extracted PDF pages…).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentLoader, InlineTextLoader>());

        services.TryAddSingleton<IEphemeralCollectionSearch>(sp =>
        {
            // Decide from the registrations, resolve at the first search (GAP-02): every
            // runner registers the RAG subsystem, and these facades are built with the
            // tool registry for every crew — resolving the pipeline and the store here
            // made a crew that never searches pay for them, and fail on an unknown
            // Orkeon:Rag:Provider.
            var registrations = sp.GetService<IServiceProviderIsService>();
            if (registrations is not null)
            {
                return registrations.IsService(typeof(IEmbeddingProvider))
                    && registrations.IsService(typeof(IIngestionPipeline))
                    && registrations.IsService(typeof(IDocumentStore))
                        ? new DeferredEphemeralCollectionSearch(() => CreateSearch(sp)!)
                        : new UnconfiguredEphemeralCollectionSearch();
            }

            return (IEphemeralCollectionSearch?)CreateSearch(sp) ?? new UnconfiguredEphemeralCollectionSearch();
        });

        return services;
    }

    /// <summary>
    /// The engine over the registered pipeline and store, or <c>null</c> when one of its
    /// collaborators is missing. The embedding port is probed first: it is the cheapest
    /// signal that the RAG stack is absent.
    /// </summary>
    private static EphemeralCollectionSearchService? CreateSearch(IServiceProvider sp)
    {
        var embeddings = sp.GetService<IEmbeddingProvider>();
        var pipeline = embeddings is null ? null : sp.GetService<IIngestionPipeline>();
        var store = embeddings is null ? null : sp.GetService<IDocumentStore>();

        if (embeddings is null || pipeline is null || store is null)
            return null;

        return new EphemeralCollectionSearchService(
            pipeline,
            store,
            embeddings,
            sp.GetService<ILogger<EphemeralCollectionSearchService>>());
    }

    /// <summary>The engine, built at the first search.</summary>
    private sealed class DeferredEphemeralCollectionSearch : IEphemeralCollectionSearch
    {
        private readonly Lazy<IEphemeralCollectionSearch> _inner;

        public DeferredEphemeralCollectionSearch(Func<IEphemeralCollectionSearch> create) => _inner = new(create);

        public Task<EphemeralSearchResult> SearchAsync(
            EphemeralSearchRequest request,
            CancellationToken cancellationToken = default)
            => _inner.Value.SearchAsync(request, cancellationToken);
    }

    /// <summary>
    /// Call-time loud failure used when the RAG subsystem is not registered:
    /// tool enumeration and construction keep working, only actual searches throw.
    /// </summary>
    private sealed class UnconfiguredEphemeralCollectionSearch : IEphemeralCollectionSearch
    {
        public Task<EphemeralSearchResult> SearchAsync(
            EphemeralSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Ephemeral RAG search is not configured. The search tools (txt_search, mdx_search, " +
                "pdf_search, directory_search) require the RAG subsystem: register it with " +
                "services.AddOrkeonRag(configuration) and provide an IEmbeddingProvider " +
                "(AddOrkeonInfrastructure() registers a default one).");
        }
    }
}
