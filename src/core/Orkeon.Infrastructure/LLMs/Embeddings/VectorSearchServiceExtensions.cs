using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.LLMs.Embeddings;

/// <summary>
/// Extension methods for registering vector search and embedding services.
/// </summary>
public static class VectorSearchServiceExtensions
{
    /// <summary>
    /// Adds Orkeon vector search services including embedding providers and configuration.
    /// </summary>
    public static IServiceCollection AddOrkeonVectorSearch(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<EmbeddingOptions>(configuration.GetSection("Orkeon:Embeddings"));
        services.Configure<VectorSearchOptions>(configuration.GetSection("Orkeon:VectorSearch"));
        services.Configure<EmbeddingCacheOptions>(configuration.GetSection("Orkeon:EmbeddingCache"));

        // Concrete providers. OpenAIEmbeddingProvider needs an M.E.AI
        // IEmbeddingGenerator<string, Embedding<float>>, which this method does not register
        // and a host may never register — so it is BUILT from the generator when one is
        // present, and simply absent otherwise.
        //
        // It used to be registered by type, unconditionally. MS.DI throws when a registered
        // service's own dependencies cannot be resolved, so `GetService<OpenAIEmbeddingProvider>()`
        // threw instead of returning null — which put BOTH graceful fallbacks (the one below and
        // DefaultEmbeddingProviderResolver's) behind an exception naming an interface the
        // operator has never heard of, at container build rather than at first embed. The whole
        // point of UnconfiguredEmbeddingProvider is an actionable message deferred to first use.
        services.AddSingleton(sp =>
        {
            var generator = sp.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
            return generator is null
                ? null!
                : new OpenAIEmbeddingProvider(generator, sp.GetRequiredService<IOptions<EmbeddingOptions>>());
        });
        services.AddHttpClient<OllamaEmbeddingProvider>();

        // Register IEmbeddingProvider with factory that selects provider based on config
        services.AddSingleton<IEmbeddingProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
#pragma warning disable CA1308 // lowercase is the normalized provider-name token driving the switch, not a comparison normalization
            IEmbeddingProvider provider = options.Provider.ToLowerInvariant() switch
#pragma warning restore CA1308
            {
                "ollama" => sp.GetRequiredService<OllamaEmbeddingProvider>(),
                _ => sp.GetService<OpenAIEmbeddingProvider>()
                     ?? (IEmbeddingProvider)new UnconfiguredEmbeddingProvider(),
            };

            if (options.EnableCache)
            {
                provider = new CachedEmbeddingProvider(
                    provider,
                    sp.GetRequiredService<IOptions<EmbeddingCacheOptions>>());
            }

            return provider;
        });

        return services;
    }
}
