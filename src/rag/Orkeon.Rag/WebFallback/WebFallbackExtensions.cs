using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    /// Registers <see cref="WebSearchDocumentRetriever"/>, its options
    /// (bound on <see cref="RagWebFallbackOptions.SectionKey"/>) and the
    /// prompt-injection validator gating every downloaded document.
    /// </summary>
    public static IServiceCollection AddOrkeonRagWebFallback(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<RagWebFallbackOptions>(configuration.GetSection(RagWebFallbackOptions.SectionKey));

        services.AddHttpClient(WebSearchDocumentRetriever.SearchClientName);
        services.AddHttpClient(WebSearchDocumentRetriever.PageClientName);

        // Concrete validator for the retriever gate. Kept distinct from the
        // IDataValidator enumerable registration owned by AddOrkeonRag (ingestion
        // chain) — TryAdd keeps both sides independent and host-overridable.
        services.TryAddSingleton<PromptInjectionDocumentValidator>();

        services.TryAddSingleton(sp => new WebSearchDocumentRetriever(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<IOptions<RagWebFallbackOptions>>(),
            sp.GetRequiredService<PromptInjectionDocumentValidator>(),
            sp.GetService<ILogger<WebSearchDocumentRetriever>>()));

        return services;
    }
}
