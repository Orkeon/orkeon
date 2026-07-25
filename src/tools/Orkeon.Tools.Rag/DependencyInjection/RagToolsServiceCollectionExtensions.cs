using Microsoft.Extensions.DependencyInjection;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.Tools;
using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Tools.Rag.DependencyInjection;

/// <summary>
/// Auto-registration of the RAG agent tools. Mirrors the sibling pattern used by
/// Orkeon.Tools.Data / Orkeon.Tools.Code / Orkeon.Tools.Web / Orkeon.Tools.EventHub
/// (see <c>AddOrkeon*Tools</c>).
/// </summary>
public static class RagToolsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RagSearchTool"/> (<c>rag_search</c>) as an
    /// <see cref="IBaseTool"/> so tool registries discover it via
    /// <c>GetServices&lt;IBaseTool&gt;()</c>. Requires the RAG subsystem
    /// (<c>AddOrkeonRag(configuration)</c> from <c>Orkeon.Rag.DependencyInjection</c>)
    /// for the <see cref="IRagPipeline"/>; picks up an <see cref="IRaggableStore"/>
    /// automatically when the RaggableTree subsystem is registered.
    /// </summary>
    public static IServiceCollection AddOrkeonRagTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // AddSingleton on purpose: multiple IBaseTool implementations cohabit in DI —
        // TryAdd* would silently drop the tool when other IBaseTool registrations exist.
        services.AddSingleton<IBaseTool>(sp => new RagSearchTool(
            sp.GetRequiredService<IRagPipeline>(),
            sp.GetService<IRaggableStore>()));

        return services;
    }
}
