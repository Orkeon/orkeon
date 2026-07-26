using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Reranking;

namespace Orkeon.Rag.DependencyInjection;

/// <summary>
/// Registers the reranking stage (RAG-04/C3): a populated
/// <see cref="RerankerFactory"/> carrying the built-in rerankers plus any
/// <see cref="IRerankerRegistrar"/> contributed by optional packages
/// (e.g. <c>AddOrkeonOnnxReranker()</c> from <c>Orkeon.Rag.Onnx</c>).
/// </summary>
public static class RagRerankingExtensions
{
    /// <summary>
    /// Registers the singleton <see cref="RerankerFactory"/> pre-populated with
    /// <c>none</c>/<c>noop</c> (<see cref="NoopReranker"/>) and
    /// <c>llm</c>/<c>listwise</c> (<see cref="LlmListwiseReranker"/> — resolves the
    /// host's <see cref="IChatClient"/> lazily, at first <c>Create("llm")</c>).
    /// Called by <c>AddOrkeonRag</c>; safe to call directly and idempotent
    /// (<c>TryAdd</c> — a host-registered factory wins).
    /// </summary>
    public static IServiceCollection AddOrkeonRagReranking(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(sp =>
        {
            var factory = new RerankerFactory();

            factory.Register(NoopReranker.RerankerName, static () => new NoopReranker(), "noop");
            factory.Register(
                LlmListwiseReranker.RerankerName,
                () => new LlmListwiseReranker(
                    sp.GetRequiredService<IChatClient>(),
                    sp.GetService<ILogger<LlmListwiseReranker>>()),
                "listwise");

            foreach (var registrar in sp.GetServices<IRerankerRegistrar>())
            {
                registrar.Register(factory, sp);
            }

            return factory;
        });

        return services;
    }
}
