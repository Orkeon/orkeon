using Microsoft.Extensions.DependencyInjection;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Evaluation;

namespace Orkeon.Tools.Rag.DependencyInjection;

/// <summary>
/// Auto-registration of the RAG agent tools. Mirrors the sibling pattern used by
/// Orkeon.Tools.Data / Orkeon.Tools.Code / Orkeon.Tools.Web / Orkeon.Tools.EventHub
/// (see <c>AddOrkeon*Tools</c>).
/// </summary>
public static class RagToolsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RagSearchTool"/> (<c>rag_search</c>),
    /// <see cref="RagIngestTool"/> (<c>rag_ingest</c>) and
    /// <see cref="RagEvalTool"/> (<c>rag_eval</c>) as <see cref="IBaseTool"/>s
    /// so tool registries discover them via <c>GetServices&lt;IBaseTool&gt;()</c>.
    /// Requires the RAG subsystem (<c>AddOrkeonRag(configuration)</c> from
    /// <c>Orkeon.Rag.DependencyInjection</c>) for the <see cref="IRagPipeline"/> /
    /// <see cref="IIngestionPipeline"/>; picks up an <see cref="IRaggableStore"/>
    /// (rag_search code-index routing) and an <see cref="IFileSystemService"/>
    /// (rag_ingest glob expansion) automatically when available.
    /// </summary>
    public static IServiceCollection AddOrkeonRagTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // AddSingleton on purpose: multiple IBaseTool implementations cohabit in DI —
        // TryAdd* would silently drop the tool when other IBaseTool registrations exist.
        services.AddSingleton<IBaseTool>(sp => new RagSearchTool(
            sp.GetRequiredService<IRagPipeline>(),
            sp.GetService<IRaggableStore>()));

        services.AddSingleton<IBaseTool>(sp => new RagIngestTool(
            sp.GetRequiredService<IIngestionPipeline>(),
            sp.GetRequiredService<IFileSystemService>()));

        services.AddSingleton<IBaseTool>(sp => new RagEvalTool(
            sp.GetRequiredService<IRagEvalHarness>()));

        return services;
    }
}
