using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Rag;
using Orkeon.Application.Rag;
using Orkeon.Infrastructure.Knowledge.Generation;

namespace Orkeon.Infrastructure.Knowledge;

/// <summary>
/// Extension methods for registering RAG pipeline services in the dependency injection container.
/// </summary>
public static class RagServiceExtensions
{
    /// <summary>
    /// Adds the Orkeon RAG pipeline and its dependencies to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration for binding options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrkeonRag(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<RagPipelineOptions>(configuration.GetSection("Orkeon:Rag"));

        services.AddScoped<IRetriever, KnowledgeRetriever>();
        services.AddSingleton<IContextAugmenter, TemplateContextAugmenter>();
        services.AddScoped<IResponseGenerator, ChatClientResponseGenerator>();
        services.AddScoped<IRagPipeline, RagPipeline>();
        services.AddScoped<RagTool>();
        // Tool registries discover tools via GetServices<IBaseTool>() — without this
        // registration rag_search is invisible to agents (RAG-01/C1).
        services.AddScoped<Domain.Tools.IBaseTool>(sp => sp.GetRequiredService<RagTool>());

        return services;
    }
}
