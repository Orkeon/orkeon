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

        // Register concrete providers
        services.AddSingleton<OpenAIEmbeddingProvider>();
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
                _ => sp.GetRequiredService<OpenAIEmbeddingProvider>()
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
