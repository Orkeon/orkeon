using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Memory;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Chunking;
using Orkeon.Rag.Configuration;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Ingestion;
using Orkeon.Rag.Loaders;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Stores;
using Orkeon.Rag.Validation;

namespace Orkeon.Rag.DependencyInjection;

/// <summary>
/// Opt-in registration of the RAG subsystem (ADR-006): loaders, ingestion-path
/// validation, pipelines, and named-component factories. Self-sufficient
/// (<c>TryAdd*</c> everywhere — the host wins) and never called from
/// <c>AddOrkeonInfrastructure</c>.
/// </summary>
/// <remarks>
/// The default <see cref="IDocumentStore"/> is a <see cref="MemoryProviderDocumentStore"/>
/// over the ambient <see cref="IMemoryProvider"/> (a host may register its own store
/// first). Setting <c>Orkeon:Rag:Provider</c> (see <see cref="RagStoreOptions"/>) backs the
/// store with a dedicated provider created through the host's
/// <see cref="IMemoryProviderFactory"/> instead — an unknown alias fails loudly with the
/// list of supported aliases. The host must provide an <see cref="IEmbeddingProvider"/> and an
/// <see cref="IChatClient"/> for the query pipeline — <c>AddOrkeonInfrastructure()</c>
/// registers semantic-first defaults for both; without them the pipelines fail loudly
/// at resolution. Named <see cref="IChunkingStrategy"/> implementations register on
/// the <see cref="ChunkingStrategyFactory"/>.
/// </remarks>
public static class RagServiceCollectionExtensions
{
    /// <summary>Configuration section bound to <see cref="RagStoreOptions"/> (document-store provider selection).</summary>
    public const string RagSectionKey = "Orkeon:Rag";

    /// <summary>Configuration section bound to <see cref="RagIngestionOptions"/>.</summary>
    public const string IngestionSectionKey = "Orkeon:Rag:Ingestion";

    /// <summary>
    /// Provider aliases accepted by <c>Orkeon:Rag:Provider</c>, mirroring the switch of
    /// the Infrastructure <c>MemoryProviderFactory</c>.
    /// </summary>
    private static readonly string[] s_knownProviderAliases =
        ["inmemory", "in-memory", "redis", "sqlite", "chromadb", "chroma", "pinecone", "lancedb", "lance"];

    /// <summary>
    /// Registers the RAG subsystem pipelines and their collaborators.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration root; the <c>Orkeon:Rag</c> tree (profile + per-key overrides, plan §8.1) and <c>Orkeon:Rag:Ingestion</c> are bound when present.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrkeonRag(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions();
        services.Configure<RagStoreOptions>(configuration.GetSection(RagSectionKey));
        services.Configure<RagIngestionOptions>(configuration.GetSection(IngestionSectionKey));

        // Effective query-pipeline options (RagOptions v2, plan §8.1): the
        // Orkeon:Rag:Profile preset overridden key by key from Orkeon:Rag.
        services.TryAddSingleton(_ => RagOptionsFactory.Build(configuration));

        // Named-component factories (chunkers/transformers/rerankers register by name).
        // The chunking factory ships pre-populated with the four canonical strategies
        // (recursive/sentence/structural/semantic) — an empty factory made every first
        // ingestion fail with "Unknown chunking strategy 'recursive'" (RAG-03 bug).
        services.TryAddSingleton(_ => ChunkingStrategyFactoryDefaults.CreateDefault());
        services.TryAddSingleton<QueryTransformerFactory>();
        services.AddOrkeonRagReranking();

        // Document loaders (VFS-backed) + selection factory.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentLoader, TextFileLoader>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentLoader, CsvDocumentLoader>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentLoader, HtmlDocumentLoader>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentLoader, PdfDocumentLoader>());

        // HTTP-backed web page loader (typed HttpClient).
        services.AddHttpClient<WebPageLoader>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDocumentLoader, WebPageLoader>(
            sp => sp.GetRequiredService<WebPageLoader>()));

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

        // Default document store: memory-provider-backed (capability-aware — native
        // collections / scored vector search when the provider supports them, prefixed
        // keys + local cosine fallback otherwise). The backing provider is either the
        // ambient IMemoryProvider or, when Orkeon:Rag:Provider is set, a dedicated
        // provider created by the existing IMemoryProviderFactory (unknown alias = loud
        // failure, never a silent fallback). A host-registered IDocumentStore wins.
        services.TryAddSingleton<IDocumentStore>(sp =>
            new MemoryProviderDocumentStore(ResolveDocumentStoreProvider(sp)));

        // Hybrid retrieval (RAG-04/C2, per-profile since C4): the store is always
        // wrapped in the BM25+RRF decorator so ingestion feeds the lexical index;
        // whether a search fuses is decided per query (profile presets), with
        // Orkeon:Rag:Retrieval:Hybrid:Enabled as the default mode.
        services.AddOrkeonHybridRetrieval(configuration, ResolveDocumentStoreProvider);

        // Per-collection ingestion manifests (incremental state, RAG-03/C1):
        // one JSON file per collection, written through the VFS.
        services.TryAddSingleton<IIngestionManifestStore>(sp => new FileIngestionManifestStore(
            sp.GetRequiredService<IFileSystemService>(),
            sp.GetRequiredService<IOptions<RagIngestionOptions>>().Value,
            sp.GetService<ILogger<FileIngestionManifestStore>>()));

        // Pipelines (façades of the subsystem).
        services.TryAddSingleton<IIngestionPipeline>(sp => new DefaultIngestionPipeline(
            sp.GetRequiredService<DocumentLoaderFactory>(),
            sp.GetRequiredService<ChunkingStrategyFactory>(),
            sp.GetRequiredService<IEmbeddingProvider>(),
            sp.GetRequiredService<IDocumentStore>(),
            sp.GetRequiredService<DataValidationPipeline>(),
            sp.GetRequiredService<IIngestionManifestStore>(),
            sp.GetRequiredService<IOptions<RagIngestionOptions>>().Value,
            sp.GetService<ILogger<DefaultIngestionPipeline>>()));

        // Default query pipeline: a StagedRagPipeline composed from the effective
        // RagOptions (profile preset + configuration overrides). A host-registered
        // IRagPipeline wins (TryAdd).
        services.TryAddSingleton<IRagPipeline>(sp =>
            CreateStagedPipeline(sp, sp.GetRequiredService<Abstractions.Options.RagOptions>()));

        // Preset-aware profile resolution (fast/balanced/quality → one memoized
        // pipeline per profile; 'default' → the registered IRagPipeline above).
        services.TryAddSingleton<IRagProfileResolver>(sp => new ProfileRagPipelineResolver(
            configuration,
            options => CreateStagedPipeline(sp, options),
            sp.GetRequiredService<IRagPipeline>));

        // Knowledge attachments → prompt injection with citations (RAG-03/C4):
        // the execution orchestrator picks the augmenter up when present.
        services.AddOrkeonKnowledgeAugmentation();

        // Evaluation harness (RAG-04/C1): golden datasets, recall@k/MRR, labelled judge.
        services.AddOrkeonRagEvaluation();

        // YAML `rag:` crew block → collections ingested at crew load (RAG-03/C3);
        // the incremental manifest makes a fresh collection a no-op.
        services.TryAddSingleton<IRagCollectionsBootstrapper>(sp => new RagCollectionsBootstrapper(
            sp.GetRequiredService<IIngestionPipeline>(),
            sp.GetService<ILogger<RagCollectionsBootstrapper>>()));

        return services;
    }

    /// <summary>
    /// Builds a <see cref="StagedRagPipeline"/> over the shared collaborators for
    /// <paramref name="options"/> — the single construction path used by both the
    /// default <see cref="IRagPipeline"/> registration and the profile resolver.
    /// </summary>
    private static StagedRagPipeline CreateStagedPipeline(
        IServiceProvider sp,
        Abstractions.Options.RagOptions options)
        => new(
            sp.GetRequiredService<IDocumentStore>(),
            sp.GetRequiredService<IEmbeddingProvider>(),
            sp.GetRequiredService<IChatClient>(),
            options,
            sp.GetRequiredService<QueryTransformerFactory>(),
            sp.GetRequiredService<RerankerFactory>(),
            sp.GetService<IGroundednessChecker>(),
            sp.GetService<ILogger<StagedRagPipeline>>());

    /// <summary>
    /// Resolves the memory provider backing the default document store: the ambient
    /// <see cref="IMemoryProvider"/> when <see cref="RagStoreOptions.Provider"/> is unset,
    /// otherwise a provider created by the host's <see cref="IMemoryProviderFactory"/>
    /// from the <c>Orkeon:Rag</c> configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <see cref="RagStoreOptions.Provider"/> is set to an unknown alias — the error
    /// message lists every supported alias (never a silent fallback).
    /// </exception>
    private static IMemoryProvider ResolveDocumentStoreProvider(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<RagStoreOptions>>().Value;

        if (string.IsNullOrWhiteSpace(options.Provider))
            return serviceProvider.GetRequiredService<IMemoryProvider>();

#pragma warning disable CA1308 // lowercase is the factory's alias form, not a comparison normalization
        var alias = options.Provider.Trim().ToLowerInvariant();
#pragma warning restore CA1308

        if (!s_knownProviderAliases.Contains(alias, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unknown RAG document-store provider '{options.Provider}' (configuration key " +
                $"'{RagSectionKey}:Provider'). Supported aliases: {string.Join(", ", s_knownProviderAliases)}.");
        }

        var factory = serviceProvider.GetRequiredService<IMemoryProviderFactory>();
        var config = new Application.Memory.MemoryProviderConfigDto(
            alias,
            options.ConnectionString ?? string.Empty,
            options.ProviderOptions.Count > 0
                ? options.ProviderOptions.ToDictionary(pair => pair.Key, pair => (object)pair.Value)
                : null);

        return factory.Create(config, serviceProvider.GetService<ILoggerFactory>());
    }
}
