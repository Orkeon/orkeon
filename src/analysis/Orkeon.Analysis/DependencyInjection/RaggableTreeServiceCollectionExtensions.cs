using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.DependencyInjection;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.Core.ContextInjection;
using Orkeon.Analysis.Fingerprinters;
using Orkeon.Analysis.Summarizers;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Analysis.Vectors;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Analysis.DependencyInjection;

public static class RaggableTreeServiceCollectionExtensions
{
    private const string DefaultOpenAiBaseUrl = "https://api.openai.com/";
    private const string DefaultOllamaBaseUrl = "http://localhost:11434/";

    public static IServiceCollection AddRaggableTree(
        this IServiceCollection services,
        RaggableTreeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        options ??= new RaggableTreeOptions();

        services.TryAddSingleton(options);

        if (!options.Enabled) return services;

        RegisterCoreComponents(services);
        RegisterLanguageAdapters(services);
        RegisterFingerprinters(services);
        RegisterSummarizer(services, options);
        RegisterEmbeddingProvider(services, options);
        RegisterStore(services);
        RegisterBuilder(services);
        RegisterCitationValidator(services, options);

        return services;
    }

    private static void RegisterCoreComponents(IServiceCollection services)
    {
        services.TryAddSingleton<IFileSystemDiscoverer>(sp =>
            new FileSystemDiscoverer(sp.GetRequiredService<IFileSystemService>()));
        services.TryAddSingleton<TreeSitterParserPool>();
        services.TryAddSingleton<IReferenceResolver, DefaultReferenceResolver>();
        services.TryAddSingleton<IEmbeddingTextComposer, EmbeddingTextComposer>();
    }

    private static void RegisterLanguageAdapters(IServiceCollection services)
    {
        foreach (var adapter in LanguageAdapterFactory.GetAll())
        {
            services.AddSingleton(adapter);
        }
    }

    private static void RegisterFingerprinters(IServiceCollection services)
    {
        services.AddSingleton<IFrameworkFingerprinter>(_ => AngularRules.Create());
        services.AddSingleton<IFrameworkFingerprinter>(_ => NestJsRules.Create());
        services.AddSingleton<IFrameworkFingerprinter>(_ => AspNetRules.Create());
        services.AddSingleton<IFrameworkFingerprinter>(_ => FlaskRules.Create());
        services.AddSingleton<IFrameworkFingerprinter>(_ => FastApiRules.Create());
    }

    private static void RegisterSummarizer(IServiceCollection services, RaggableTreeOptions options)
    {
        if (options.EnrichWithLlm && options.Summarizer.Provider != SummarizerProviderKind.None)
        {
            services.TryAddSingleton<INodeSummarizer>(sp =>
            {
                var llm = sp.GetRequiredService<ILlmProvider>();
                var summarizerOptions = new LlmNodeSummarizerOptions
                {
                    Model = options.Summarizer.Model,
                    Concurrency = options.Summarizer.Concurrency,
                };
                var composer = sp.GetService<IEmbeddingTextComposer>();
                return new LlmNodeSummarizer(llm, summarizerOptions, composer);
            });
        }
        else
        {
            services.TryAddSingleton<INodeSummarizer>(NullNodeSummarizer.Instance);
        }
    }

    private static void RegisterEmbeddingProvider(IServiceCollection services, RaggableTreeOptions options)
    {
        switch (options.Embedding.Provider)
        {
            case EmbeddingProviderKind.OpenAI:
                services.TryAddSingleton<IEmbeddingProvider>(sp =>
                    new OpenAIEmbeddingProvider(
                        new OpenAIEmbeddingOptions
                        {
                            ApiKey = options.Embedding.ApiKey
                                ?? throw new InvalidOperationException(
                                    "RaggableTree: OpenAI embedding requires Embedding.ApiKey."),
                            // Provider-scoped default model: applied only when the user did
                            // not override Embedding.Model (kept empty by EmbeddingOptions).
                            Model = string.IsNullOrEmpty(options.Embedding.Model)
                                ? "text-embedding-3-small"
                                : options.Embedding.Model,
                            BaseUrl = options.Embedding.BaseUrl ?? new Uri(DefaultOpenAiBaseUrl),
                            // Default dimension for OpenAI text-embedding-3-small.
                            // Other OpenAI models (text-embedding-3-large = 3072,
                            // ada-002 = 1536) require an explicit Embedding.Dimensions
                            // override. This fallback is provider-scoped — it does NOT
                            // apply to the generic IEmbeddingProvider contract, which
                            // exposes its actual Dimensions independently.
                            Dimensions = options.Embedding.Dimensions ?? 1536,
                            MaxTextChars = options.Embedding.MaxTextChars,
                        },
                        logger: sp.GetService<ILogger<OpenAIEmbeddingProvider>>()));
                break;

            case EmbeddingProviderKind.Ollama:
                services.TryAddSingleton<IEmbeddingProvider>(_ =>
                    new OllamaEmbeddingProvider(new OllamaEmbeddingOptions
                    {
                        // Provider-scoped default model: applied only when the user did
                        // not override Embedding.Model (kept empty by EmbeddingOptions).
                        Model = string.IsNullOrEmpty(options.Embedding.Model)
                            ? "nomic-embed-text"
                            : options.Embedding.Model,
                        BaseUrl = options.Embedding.BaseUrl ?? new Uri(DefaultOllamaBaseUrl),
                        // Default dimension for Ollama nomic-embed-text. Other Ollama
                        // models (mxbai-embed-large = 1024, all-minilm = 384) require
                        // an explicit Embedding.Dimensions override. Provider-scoped
                        // fallback only.
                        Dimensions = options.Embedding.Dimensions ?? 768,
                    }));
                break;

            case EmbeddingProviderKind.LocalSmartComponents:
                var local = options.Embedding.Local ?? new LocalEmbeddingOptions();
                services.TryAddSingleton(local);
                services.TryAddSingleton<IEmbeddingProvider>(sp =>
                    TryCreateLocalProvider(sp, local)
                        ?? throw new InvalidOperationException(
                            "RaggableTree: provider 'LocalSmartComponents' requires the package "
                            + "'Orkeon.Tools.Embeddings.Local' to be referenced by the host project. "
                            + "Either add a PackageReference / ProjectReference to it, or call "
                            + "services.AddOrkeonLocalEmbeddings(...) before AddRaggableTree(...)."));
                break;

            case EmbeddingProviderKind.Onnx:
            case EmbeddingProviderKind.None:
            default:
                break;
        }
    }

    /// <summary>
    /// Attempts to instantiate <c>Orkeon.Tools.Embeddings.Local.LocalEmbeddingProvider</c> via
    /// reflection so <c>Orkeon.Analysis</c> does not take a compile-time dependency on the
    /// opt-in local-embeddings package. Returns <see langword="null"/> when the assembly is
    /// not loaded — the caller surfaces an actionable error in that case.
    /// </summary>
    /// <remarks>
    /// Constructor signature must mirror
    /// <c>LocalEmbeddingProvider(IFileSystemService, LocalEmbeddingOptions?, ILogger&lt;LocalEmbeddingProvider&gt;?)</c>.
    /// </remarks>
    private static IEmbeddingProvider? TryCreateLocalProvider(
        IServiceProvider sp, LocalEmbeddingOptions options)
    {
        var type = Type.GetType(
            "Orkeon.Tools.Embeddings.Local.LocalEmbeddingProvider, Orkeon.Tools.Embeddings.Local",
            throwOnError: false);
        if (type is null) return null;

        var loggerType = typeof(ILogger<>).MakeGenericType(type);
        var instance = Activator.CreateInstance(
            type,
            sp.GetRequiredService<IFileSystemService>(),
            options,
            sp.GetService(loggerType));
        return instance as IEmbeddingProvider;
    }

    private static void RegisterStore(IServiceCollection services)
    {
        services.TryAddSingleton(sp =>
        {
            var queryEmbedder = CreateQueryEmbedder(sp.GetService<IEmbeddingProvider>());
            return new InMemoryRaggableStore(
                [], [], sp.GetRequiredService<IFileSystemService>(), queryEmbedder: queryEmbedder);
        });
        services.TryAddSingleton<IRaggableStore>(sp => sp.GetRequiredService<InMemoryRaggableStore>());
        services.TryAddSingleton<ICodebaseContextProvider>(sp =>
            new CodebaseContextProvider(sp.GetRequiredService<IRaggableStore>()));
    }

    private static Func<string, CancellationToken, Task<ReadOnlyMemory<float>?>>? CreateQueryEmbedder(
        IEmbeddingProvider? embedder)
    {
        if (embedder is null) return null;

        var cache = new System.Collections.Concurrent.ConcurrentDictionary<string, ReadOnlyMemory<float>>(StringComparer.Ordinal);
        const int MaxEntries = 100;

        return async (text, ct) =>
        {
            if (string.IsNullOrEmpty(text)) return null;
            if (cache.TryGetValue(text, out var cached)) return cached;
            var vectors = await embedder.EmbedBatchAsync([text], ct).ConfigureAwait(false);
            if (vectors.Count == 0) return null;
            if (cache.Count >= MaxEntries) cache.Clear(); // simple bounded eviction
            cache[text] = vectors[0];
            return vectors[0];
        };
    }

    private static void RegisterBuilder(IServiceCollection services)
    {
        services.AddTransient(sp => new RaggableTreeBuilder(
            sp.GetServices<ILanguageAdapter>(),
            sp.GetRequiredService<IFileSystemDiscoverer>(),
            sp.GetRequiredService<IFileSystemService>(),
            sp.GetRequiredService<TreeSitterParserPool>(),
            sp.GetRequiredService<IReferenceResolver>(),
            sp.GetRequiredService<IEmbeddingTextComposer>(),
            new RaggableEnrichmentServices
            {
                Fingerprinters = sp.GetServices<IFrameworkFingerprinter>(),
                Summarizer = sp.GetService<INodeSummarizer>(),
                Embedder = sp.GetService<IEmbeddingProvider>(),
                VectorStore = sp.GetService<IVectorStoreProvider>(),
            }));

        services.AddTransient<Func<RaggableTreeBuilder>>(sp =>
            () => sp.GetRequiredService<RaggableTreeBuilder>());
    }

    private static void RegisterCitationValidator(IServiceCollection services, RaggableTreeOptions options)
    {
        if (!options.ValidateCitations) return;

        services.TryAddSingleton<ICitationBlockValidator>(sp =>
            new CitationBlockValidator(sp.GetRequiredService<IRaggableStore>()));

        services.TryAddSingleton<IInlineFqnValidator>(sp =>
            new InlineFqnValidator(sp.GetRequiredService<IRaggableStore>()));
    }
}
