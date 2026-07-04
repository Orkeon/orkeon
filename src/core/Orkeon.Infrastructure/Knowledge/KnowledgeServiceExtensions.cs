using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.Knowledge.Chunking;
using Orkeon.Infrastructure.Knowledge.Loaders;

namespace Orkeon.Infrastructure.Knowledge;

/// <summary>
/// Extension methods for registering knowledge services in the DI container.
/// </summary>
public static class KnowledgeServiceExtensions
{
    /// <summary>
    /// Adds knowledge source infrastructure services: document loaders, text chunkers,
    /// and the knowledge service.
    /// </summary>
    public static IServiceCollection AddOrkeonKnowledge(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        // Register document loaders
        services.AddSingleton<IDocumentLoader, TextFileLoader>();
        services.AddSingleton<IDocumentLoader, CsvDocumentLoader>();
        services.AddSingleton<IDocumentLoader, HtmlDocumentLoader>();
        services.AddSingleton<IDocumentLoader, PdfDocumentLoader>();

        // WebPageLoader requires HttpClient, registered via factory
        services.AddHttpClient<WebPageLoader>();
        services.AddSingleton<IDocumentLoader>(sp =>
            sp.GetRequiredService<WebPageLoader>());

        // Register loader factory
        services.AddSingleton<IDocumentLoaderFactory, DocumentLoaderFactory>();

        // Register text chunker (RecursiveTextChunker as default)
        services.AddSingleton<ITextChunker, RecursiveTextChunker>();

        // Register knowledge service
        services.AddSingleton<IKnowledgeService, KnowledgeService>();

        return services;
    }
}
