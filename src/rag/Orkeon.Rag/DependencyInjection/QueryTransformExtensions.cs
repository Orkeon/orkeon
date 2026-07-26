using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orkeon.Rag.Factories;
using Orkeon.Rag.QueryTransform;

namespace Orkeon.Rag.DependencyInjection;

/// <summary>
/// Registers the query-transform stage (RAG-05/C1): a populated
/// <see cref="QueryTransformerFactory"/> carrying the built-in transformers —
/// <c>none</c> (identity), <c>multi-query</c>, <c>rag-fusion</c>, <c>hyde</c> —
/// resolved by <c>RagOptions.QueryTransform.Mode</c>. The stage options
/// (<c>Mode</c>, <c>VariantCount</c>) live in the <c>Orkeon:Rag:QueryTransform</c>
/// node of the <c>RagOptions</c> tree bound by <c>AddOrkeonRag</c>.
/// </summary>
public static class QueryTransformExtensions
{
    /// <summary>
    /// Registers the singleton <see cref="QueryTransformerFactory"/> pre-populated
    /// via <see cref="QueryTransformFactoryDefaults"/>. The LLM-backed transformers
    /// resolve the host's <see cref="IChatClient"/> lazily, at first
    /// <c>Create(mode)</c> — <c>none</c> needs no chat client at all. Called by
    /// <c>AddOrkeonRag</c>; safe to call directly and idempotent (<c>TryAdd</c> —
    /// a host-registered factory wins).
    /// </summary>
    public static IServiceCollection AddOrkeonQueryTransforms(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(sp => QueryTransformFactoryDefaults.CreateDefault(
            () => sp.GetRequiredService<IChatClient>(),
            sp.GetService<ILoggerFactory>()));

        return services;
    }
}
