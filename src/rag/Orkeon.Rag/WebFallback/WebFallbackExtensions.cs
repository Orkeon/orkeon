using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Tools.Security;
using Orkeon.Rag.Corrective;
using Orkeon.Rag.Validation;

namespace Orkeon.Rag.WebFallback;

/// <summary>
/// Opt-in registration of the secure RAG web search fallback (RAG-06/C1).
/// Every registration is <c>TryAdd*</c> so a host-provided implementation wins.
/// The fallback stays inert (<c>Enabled=false</c>) unless the
/// <c>Orkeon:Rag:WebFallback</c> section explicitly turns it on.
/// </summary>
public static class WebFallbackExtensions
{
    /// <summary>
    /// Registers <see cref="WebSearchDocumentRetriever"/>, its transport options
    /// (bound on <see cref="WebSearchRetrieverOptions.SectionKey"/>) and the
    /// prompt-injection validator gating every downloaded document. When — and
    /// only when — the transport is enabled AND an endpoint is configured, the
    /// <see cref="IWebDocumentRetriever"/> adapter is also registered so the
    /// corrective graph (RAG-06/C1) can reach its <c>web_fallback</c> node;
    /// otherwise no adapter exists and the edge stays skipped and traced.
    /// </summary>
    public static IServiceCollection AddOrkeonRagWebFallback(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<WebSearchRetrieverOptions>(configuration.GetSection(WebSearchRetrieverOptions.SectionKey));

        services.AddHttpClient(WebSearchDocumentRetriever.SearchClientName);

        // Result pages are fetched from URLs a search engine chose, so the page
        // client refuses redirects: the SSRF verdict must hold for the address
        // actually contacted, not only for the one that was validated.
        services.AddHttpClient(WebSearchDocumentRetriever.PageClientName)
            .ConfigurePrimaryHttpMessageHandler(CreateRedirectFreeHandler);

        // Concrete validator for the retriever gate. Kept distinct from the
        // IDataValidator enumerable registration owned by AddOrkeonRag (ingestion
        // chain) — TryAdd keeps both sides independent and host-overridable.
        services.TryAddSingleton<PromptInjectionDocumentValidator>();

        services.TryAddSingleton(sp => new WebSearchDocumentRetriever(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IOptions<WebSearchRetrieverOptions>>(),
            sp.GetRequiredService<PromptInjectionDocumentValidator>(),
            sp.GetService<IUrlValidator>(),
            sp.GetService<ILogger<WebSearchDocumentRetriever>>()));

        // Corrective-graph seam (6D): the IWebDocumentRetriever adapter is
        // registered only when the transport is actually usable — a disabled or
        // endpoint-less fallback must leave the corrective graph without a
        // retriever, so its web_fallback routing skips (and traces) the node
        // instead of firing dead HTTP calls. Note the second, pipeline-side
        // switch: Orkeon:Rag:Corrective:WebFallback:Enabled must ALSO be on for
        // the graph to route here (see WebSearchRetrieverOptions remarks).
        var transport = new WebSearchRetrieverOptions();
        configuration.GetSection(WebSearchRetrieverOptions.SectionKey).Bind(transport);
        if (transport.Enabled && !string.IsNullOrWhiteSpace(transport.Endpoint))
        {
            services.TryAddSingleton<IWebDocumentRetriever>(sp =>
                new WebSearchDocumentRetrieverAdapter(
                    sp.GetRequiredService<WebSearchDocumentRetriever>()));
        }

        return services;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "The IHttpClientFactory takes ownership of the primary handler it is given and disposes it when the handler chain expires.")]
    private static HttpMessageHandler CreateRedirectFreeHandler() =>
        new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
}
