using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Rag.Embeddings;
using AnalysisEmbeddingProvider = Orkeon.Analysis.Abstractions.Interfaces.IEmbeddingProvider;

namespace Orkeon.Infrastructure.LLMs.Embeddings;

/// <summary>
/// Default resolution of the Application port <see cref="IEmbeddingProvider"/> used by
/// <c>AddOrkeonInfrastructure()</c> when the host registers no provider explicitly
/// (RAG-01/C4 — « mort du stub hash silencieux »).
/// </summary>
/// <remarks>
/// <para>Resolution order, evaluated lazily at the port's first resolution:</para>
/// <list type="number">
///   <item><description>
///     <b>Local / Analysis-side provider</b> — when the container holds an
///     Analysis-side <see cref="AnalysisEmbeddingProvider"/> (e.g. the on-device
///     BGE-micro-v2 registered by <c>AddOrkeonLocalEmbeddings()</c>, or a RaggableTree
///     provider), it is wrapped in <see cref="AnalysisEmbeddingProviderAdapter"/>.
///     Detection is container-based, not reflection-based: instantiating
///     <c>LocalEmbeddingProvider</c> by reflection would require duplicating the host's
///     options and <c>IFileSystemService</c>, would boot a second ONNX session outside
///     container ownership (no disposal), and would silently drift if the constructor
///     changes. Resolving what the host actually registered is robust and honors DI.
///   </description></item>
///   <item><description>
///     <b>Configured remote provider</b> — when an <c>Orkeon:Embeddings</c> configuration
///     section exists, the matching provider is built (mirroring
///     <see cref="VectorSearchServiceExtensions.AddOrkeonVectorSearch"/> semantics,
///     including the <see cref="EmbeddingOptions.EnableCache"/> decorator).
///   </description></item>
///   <item><description>
///     <b>Fail-fast</b> — otherwise an <see cref="UnconfiguredEmbeddingProvider"/> is
///     returned; it throws an actionable <see cref="InvalidOperationException"/> at the
///     first embed call. No silent hash fallback remains
///     (<c>Stubs.HashBasedEmbeddingProvider</c> is a test double only).
///   </description></item>
/// </list>
/// </remarks>
public static partial class DefaultEmbeddingProviderResolver
{
    /// <summary>Configuration section driving the remote-provider branch.</summary>
    public const string EmbeddingsSectionKey = "Orkeon:Embeddings";

    /// <summary>Configuration section for the optional embedding cache decorator.</summary>
    public const string EmbeddingCacheSectionKey = "Orkeon:EmbeddingCache";

    /// <summary>Named <see cref="HttpClient"/> used when building the Ollama provider directly.</summary>
    internal const string OllamaHttpClientName = "Orkeon.Embeddings.Ollama";

#pragma warning disable S1075 // Canonical vendor API root used as a documented fallback default; configuration overrides it. Not a filesystem path — the VFS policy does not apply.
    /// <summary>Default Ollama base URL when the host configured none.</summary>
    private const string DefaultOllamaBaseUrl = "http://localhost:11434/";
#pragma warning restore S1075

    /// <summary>
    /// Resolves the default <see cref="IEmbeddingProvider"/> for the given container.
    /// Never throws at resolution time for the "nothing configured" case — the
    /// fail-fast provider defers its exception to the first embed call.
    /// </summary>
    /// <param name="serviceProvider">The host container.</param>
    /// <returns>The provider selected by the resolution chain described in the class remarks.</returns>
    public static IEmbeddingProvider Resolve(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var logger = serviceProvider.GetService<ILoggerFactory>()
            ?.CreateLogger("Orkeon.Infrastructure.LLMs.Embeddings.DefaultEmbeddingProviderResolver");

        // 1 — Local / Analysis-side provider (AddOrkeonLocalEmbeddings, RaggableTree, ...)
        var analysisProvider = serviceProvider.GetService<AnalysisEmbeddingProvider>();
        if (analysisProvider is not null)
        {
            if (logger is not null)
                LogAdaptedAnalysisProvider(logger, analysisProvider.GetType().Name);
            return new AnalysisEmbeddingProviderAdapter(analysisProvider);
        }

        // 2 — Remote provider configured under Orkeon:Embeddings
        var configured = TryCreateConfiguredProvider(serviceProvider, logger);
        if (configured is not null)
            return configured;

        // 3 — Fail-fast at first use (never silently hash-based)
        if (logger is not null)
            LogNoProviderConfigured(logger);
        return new UnconfiguredEmbeddingProvider();
    }

    /// <summary>
    /// Builds the provider declared under <see cref="EmbeddingsSectionKey"/>, or returns
    /// <see langword="null"/> when no configuration (or no <see cref="IConfiguration"/> at all)
    /// is present.
    /// </summary>
    private static IEmbeddingProvider? TryCreateConfiguredProvider(
        IServiceProvider serviceProvider,
        ILogger? logger)
    {
        var configuration = serviceProvider.GetService<IConfiguration>();
        var section = configuration?.GetSection(EmbeddingsSectionKey);
        if (section is null || !section.Exists())
            return null;

        var options = section.Get<EmbeddingOptions>() ?? new EmbeddingOptions();

        var provider = CreateRemoteProvider(serviceProvider, options, logger);

        if (options.EnableCache)
        {
            var cacheOptions = configuration!.GetSection(EmbeddingCacheSectionKey).Get<EmbeddingCacheOptions>()
                ?? new EmbeddingCacheOptions();
            provider = new CachedEmbeddingProvider(provider, Options.Create(cacheOptions));
        }

        return provider;
    }

    private static IEmbeddingProvider CreateRemoteProvider(
        IServiceProvider serviceProvider,
        EmbeddingOptions options,
        ILogger? logger)
    {
#pragma warning disable CA1308 // lowercase is the normalized provider-name token driving the switch, not a comparison normalization
        var providerToken = options.Provider.ToLowerInvariant();
#pragma warning restore CA1308

        if (providerToken == "ollama")
        {
            // Prefer the typed-client instance when AddOrkeonVectorSearch() registered it.
            var registered = serviceProvider.GetService<OllamaEmbeddingProvider>();
            if (registered is not null)
                return registered;

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(OllamaHttpClientName);
            httpClient.BaseAddress ??= new Uri(DefaultOllamaBaseUrl);

            if (logger is not null)
                LogConfiguredRemoteProvider(logger, "ollama", options.Model);
            return new OllamaEmbeddingProvider(httpClient, Options.Create(options));
        }

        // Default branch: OpenAI / any M.E.AI-compatible generator.
        var registeredOpenAi = serviceProvider.GetService<OpenAIEmbeddingProvider>();
        if (registeredOpenAi is not null)
            return registeredOpenAi;

        var generator = serviceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        if (generator is null)
        {
            // Misconfiguration: config declares a provider we cannot build. Stay loud but
            // defer the throw to first use, mirroring the fail-fast contract.
            if (logger is not null)
                LogConfiguredProviderMissingGenerator(logger, options.Provider);
            return new UnconfiguredEmbeddingProvider(
                $"Orkeon:Embeddings declares the provider '{options.Provider}' but no " +
                "IEmbeddingGenerator<string, Embedding<float>> is registered; register an " +
                "M.E.AI generator (or call AddOrkeonVectorSearch(configuration)), or add " +
                "AddOrkeonLocalEmbeddings() for local embedding.");
        }

        if (logger is not null)
            LogConfiguredRemoteProvider(logger, options.Provider, options.Model);
        return new OpenAIEmbeddingProvider(generator, Options.Create(options));
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Default IEmbeddingProvider: adapting Analysis-side provider {ProviderType} via AnalysisEmbeddingProviderAdapter.")]
    private static partial void LogAdaptedAnalysisProvider(ILogger logger, string providerType);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Default IEmbeddingProvider: using remote provider '{Provider}' (model '{Model}') from Orkeon:Embeddings configuration.")]
    private static partial void LogConfiguredRemoteProvider(ILogger logger, string provider, string model);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "No semantic embedding provider configured — IEmbeddingProvider will throw at first use. " +
            "Add AddOrkeonLocalEmbeddings() or configure Orkeon:Embeddings.")]
    private static partial void LogNoProviderConfigured(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Orkeon:Embeddings declares provider '{Provider}' but no IEmbeddingGenerator<string, Embedding<float>> is registered — IEmbeddingProvider will throw at first use.")]
    private static partial void LogConfiguredProviderMissingGenerator(ILogger logger, string provider);
}
