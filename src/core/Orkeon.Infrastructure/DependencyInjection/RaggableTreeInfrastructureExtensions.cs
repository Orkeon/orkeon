using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.DependencyInjection;
using Orkeon.Analysis.Vectors;
using Orkeon.Application.Interfaces.Logging;
using Orkeon.Infrastructure.Logging;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Infrastructure-side extensions for RaggableTree that require knowledge of both
/// <c>Orkeon.Analysis</c> and <c>Orkeon.Infrastructure</c>.
/// <para>
/// <c>Orkeon.Analysis</c> does not reference <c>Orkeon.Infrastructure</c>, so the embedding
/// provider logging wiring can only be done here, at the infrastructure layer.
/// </para>
/// </summary>
public static class RaggableTreeInfrastructureExtensions
{
    /// <summary>Default base URI for the OpenAI embeddings API when none is configured.</summary>
    private const string DefaultOpenAIEmbeddingBaseUrl = "https://api.openai.com/";

    /// <summary>Default base URI for a local Ollama server when none is configured.</summary>
    private const string DefaultOllamaEmbeddingBaseUrl = "http://localhost:11434/";

    /// <summary>
    /// Registers the RaggableTree services and, when an embedding provider is configured,
    /// replaces the plain <see cref="IEmbeddingProvider"/> registration with one that routes
    /// HTTP calls through <see cref="LlmLoggingDelegatingHandler"/> so that every
    /// <c>/v1/embeddings</c> request appears in the <c>llm-exchanges-*.jsonl</c> log.
    /// </summary>
    /// <remarks>
    /// Call <c>services.AddLlmExchangeLogging(...)</c> before this method to ensure
    /// <see cref="ILlmExchangeLogger"/> is registered; otherwise the handler falls back
    /// to a no-op structured logger.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="options">RaggableTree configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRaggableTreeWithLogging(
        this IServiceCollection services,
        RaggableTreeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        // Step 1 — register all base RaggableTree services (embedding provider included).
        services.AddRaggableTree(options);

        // Step 2 — if an embedding provider was registered, replace it with a logged version.
        if (options.Embedding.Provider == EmbeddingProviderKind.None)
            return services;

        switch (options.Embedding.Provider)
        {
            case EmbeddingProviderKind.OpenAI:
            {
                var embedOpts = new OpenAIEmbeddingOptions
                {
                    ApiKey = options.Embedding.ApiKey
                        ?? throw new InvalidOperationException(
                            "RaggableTree: OpenAI embedding requires Embedding.ApiKey."),
                    Model = options.Embedding.Model,
                    BaseUrl = options.Embedding.BaseUrl ?? new Uri(DefaultOpenAIEmbeddingBaseUrl),
                    Dimensions = options.Embedding.Dimensions ?? 1536,
                    MaxTextChars = options.Embedding.MaxTextChars,
                };

                services.Replace(ServiceDescriptor.Singleton<IEmbeddingProvider>(sp =>
                {
                    var handler = BuildLoggingHandler(sp);
                    var logger = sp.GetService<ILogger<OpenAIEmbeddingProvider>>();
                    return new OpenAIEmbeddingProvider(embedOpts, handler, logger);
                }));
                break;
            }

            case EmbeddingProviderKind.Ollama:
            {
                var embedOpts = new OllamaEmbeddingOptions
                {
                    Model = options.Embedding.Model,
                    BaseUrl = options.Embedding.BaseUrl ?? new Uri(DefaultOllamaEmbeddingBaseUrl),
                    Dimensions = options.Embedding.Dimensions ?? 768,
                };

                services.Replace(ServiceDescriptor.Singleton<IEmbeddingProvider>(sp =>
                {
                    var handler = BuildLoggingHandler(sp);
                    return new OllamaEmbeddingProvider(embedOpts, handler);
                }));
                break;
            }
        }

        return services;
    }

    /// <summary>
    /// Creates a <see cref="LlmLoggingDelegatingHandler"/> wired to an inner
    /// <see cref="HttpClientHandler"/>. Resolves <see cref="ILlmExchangeLogger"/> from DI
    /// when available; falls back to a structured-logger-only logger otherwise.
    /// </summary>
    private static LlmLoggingDelegatingHandler BuildLoggingHandler(IServiceProvider sp)
    {
        var logger = sp.GetRequiredService<ILogger<LlmLoggingDelegatingHandler>>();
        var exchangeLogger = sp.GetService<ILlmExchangeLogger>()
            ?? new LlmExchangeStructuredLogger(
                sp.GetRequiredService<ILogger<LlmExchangeStructuredLogger>>());

        // Pick up the same LlmLoggingOptions registered by AddLlmExchangeLogging
        // (defaults if absent) so embedding HTTP traffic honours
        // FullEmbeddingLog / MaxBodyLengthChars / LogStreamingExchanges.
        var options = sp.GetService<LlmLoggingOptions>();

        return new LlmLoggingDelegatingHandler(exchangeLogger, logger, options)
        {
            InnerHandler = new HttpClientHandler(),
        };
    }
}
