using Microsoft.Extensions.DependencyInjection;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Options;
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
    /// <remarks>
    /// Idempotent: every runner host built on <c>RunnerHost</c> already calls it, so a
    /// second call (a host's own <c>configureServices</c>) adds nothing. The pipelines are
    /// resolved at a tool's first call, never when the tool is built, and
    /// <c>rag_search</c> / <c>rag_eval</c> read the default collection
    /// (<see cref="RagOptions.Collection"/>, <c>Orkeon:Rag:Collection</c>) there too.
    /// </remarks>
    public static IServiceCollection AddOrkeonRagTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(RagToolsRegistrationMarker)))
            return services;
        services.AddSingleton(RagToolsRegistrationMarker.Instance);

        // AddSingleton on purpose: multiple IBaseTool implementations cohabit in DI —
        // TryAdd* would silently drop the tool when other IBaseTool registrations exist.
        services.AddSingleton<IBaseTool>(sp => new RagSearchTool(
            new DeferredRagPipeline(sp.GetRequiredService<IRagPipeline>),
            sp.GetService<IRaggableStore>(),
            () => sp.GetService<RagOptions>()?.Collection));

        services.AddSingleton<IBaseTool>(sp => new RagIngestTool(
            new DeferredIngestionPipeline(sp.GetRequiredService<IIngestionPipeline>),
            sp.GetRequiredService<IFileSystemService>()));

        services.AddSingleton<IBaseTool>(sp => new RagEvalTool(
            new DeferredRagEvalHarness(sp.GetRequiredService<IRagEvalHarness>),
            () => sp.GetService<RagOptions>()?.Collection));

        return services;
    }
}
