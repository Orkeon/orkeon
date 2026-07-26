using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Rag.Factories;

namespace Orkeon.Rag.QueryTransform;

/// <summary>
/// Registration of the built-in query transformers into a
/// <see cref="QueryTransformerFactory"/> (same pattern as
/// <c>ChunkingStrategyFactoryDefaults</c>): <c>none</c>
/// (<see cref="IdentityQueryTransformer"/>), <c>multi-query</c> (aliases
/// <c>multiquery</c>, <c>multi_query</c>), <c>rag-fusion</c> (aliases
/// <c>ragfusion</c>, <c>rag_fusion</c>) and <c>hyde</c>. Resolution of an
/// unknown name still fails loudly with the list of known names (never a
/// silent fallback).
/// </summary>
public static class QueryTransformFactoryDefaults
{
    /// <summary>
    /// Creates a factory pre-populated with the four built-in transformers.
    /// </summary>
    /// <param name="chatClient">
    /// Lazy accessor of the host's <see cref="IChatClient"/>, invoked at each
    /// LLM-backed transformer creation (never for <c>none</c>) — so a factory
    /// can be built before the chat client exists, and <c>Mode: none</c> works
    /// without one.
    /// </param>
    /// <param name="loggerFactory">Optional logger factory for the transformers' warnings.</param>
    public static QueryTransformerFactory CreateDefault(
        Func<IChatClient> chatClient,
        ILoggerFactory? loggerFactory = null)
    {
        var factory = new QueryTransformerFactory();
        return factory.RegisterDefaultTransformers(chatClient, loggerFactory);
    }

    /// <summary>
    /// Registers the four built-in transformers (and their aliases) into
    /// <paramref name="factory"/>. Third-party transformers can be registered alongside.
    /// </summary>
    /// <param name="factory">The factory to populate.</param>
    /// <param name="chatClient">See <see cref="CreateDefault"/>.</param>
    /// <param name="loggerFactory">See <see cref="CreateDefault"/>.</param>
    /// <exception cref="ArgumentException">A built-in name is already registered.</exception>
    public static QueryTransformerFactory RegisterDefaultTransformers(
        this QueryTransformerFactory factory,
        Func<IChatClient> chatClient,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(chatClient);

        factory.Register(
            IdentityQueryTransformer.TransformerName,
            static () => new IdentityQueryTransformer());
        factory.Register(
            MultiQueryTransformer.TransformerName,
            () => new MultiQueryTransformer(chatClient(), loggerFactory?.CreateLogger<MultiQueryTransformer>()),
            "multiquery", "multi_query");
        factory.Register(
            RagFusionTransformer.TransformerName,
            () => new RagFusionTransformer(chatClient(), loggerFactory?.CreateLogger<RagFusionTransformer>()),
            "ragfusion", "rag_fusion");
        factory.Register(
            HydeTransformer.TransformerName,
            () => new HydeTransformer(chatClient(), loggerFactory?.CreateLogger<HydeTransformer>()));

        return factory;
    }
}
