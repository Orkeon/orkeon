using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Validation;

namespace Orkeon.Rag.DependencyInjection;

/// <summary>
/// Opt-in registration of the RAG subsystem (ADR-006): loaders, ingestion-path
/// validation, pipelines, and named-component factories. Self-sufficient
/// (<c>TryAdd*</c> everywhere — the host wins) and never called from
/// <c>AddOrkeonInfrastructure</c>.
/// </summary>
/// <remarks>
/// The host must provide an <see cref="IDocumentStore"/> (e.g. the
/// memory-provider-backed store), an <see cref="IEmbeddingProvider"/>, and an
/// <see cref="IChatClient"/> for the query pipeline; the pipelines fail loudly at
/// resolution when those are missing. Named <see cref="IChunkingStrategy"/>
/// implementations register on the <see cref="ChunkingStrategyFactory"/>.
/// </remarks>
public static class RagServiceCollectionExtensions
{
    /// <summary>Configuration section bound to <see cref="RagIngestionOptions"/>.</summary>
    public const string IngestionSectionKey = "Orkeon:Rag:Ingestion";

    /// <summary>Configuration section bound to <see cref="LinearRagPipelineOptions"/>.</summary>
    public const string PipelineSectionKey = "Orkeon:Rag:Pipeline";

    /// <summary>
    /// Registers the RAG subsystem pipelines and their collaborators.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration root; sections <c>Orkeon:Rag:Ingestion</c> and <c>Orkeon:Rag:Pipeline</c> are bound when present.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrkeonRagPipeline(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions();
        services.Configure<RagIngestionOptions>(configuration.GetSection(IngestionSectionKey));
        services.Configure<LinearRagPipelineOptions>(configuration.GetSection(PipelineSectionKey));

        // Named-component factories (chunkers/transformers/rerankers register by name).
        services.TryAddSingleton<ChunkingStrategyFactory>();
        services.TryAddSingleton<QueryTransformerFactory>();
        services.TryAddSingleton<RerankerFactory>();

        // Document loaders (VFS-backed) + selection factory.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentLoader, TextFileLoader>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentLoader, CsvDocumentLoader>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentLoader, HtmlDocumentLoader>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentLoader, PdfDocumentLoader>());
        services.TryAddSingleton<DocumentLoaderFactory>();

        // Ingestion-path security validation (anti-injection, provenance, quarantine).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDataValidator, PromptInjectionDocumentValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDataValidator, ContentIntegrityValidator>());
        services.TryAddSingleton<IQuarantineStore, InMemoryQuarantineStore>();
        services.TryAddSingleton<IProvenanceTracker, ProvenanceTracker>();
        services.TryAddSingleton(sp => new DataValidationPipeline(
            sp.GetServices<IDataValidator>(),
            sp.GetRequiredService<IQuarantineStore>(),
            sp.GetRequiredService<IProvenanceTracker>(),
            sp.GetService<ILogger<DataValidationPipeline>>()));

        // Pipelines (façades of the subsystem).
        services.TryAddSingleton<IIngestionPipeline>(sp => new DefaultIngestionPipeline(
            sp.GetRequiredService<DocumentLoaderFactory>(),
            sp.GetRequiredService<ChunkingStrategyFactory>(),
            sp.GetRequiredService<IEmbeddingProvider>(),
            sp.GetRequiredService<IDocumentStore>(),
            sp.GetRequiredService<DataValidationPipeline>(),
            sp.GetRequiredService<IOptions<RagIngestionOptions>>().Value,
            sp.GetService<ILogger<DefaultIngestionPipeline>>()));

        services.TryAddSingleton<IRagPipeline>(sp => new LinearRagPipeline(
            sp.GetRequiredService<IDocumentStore>(),
            sp.GetRequiredService<IEmbeddingProvider>(),
            sp.GetRequiredService<IChatClient>(),
            sp.GetRequiredService<IOptions<LinearRagPipelineOptions>>().Value,
            sp.GetService<ILogger<LinearRagPipeline>>()));

        return services;
    }
}
