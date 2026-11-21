using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
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
    /// subsystem's <see cref="IChatClient"/> (<see cref="RagLlm"/>) lazily, at first <c>Create("llm")</c>).
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
                    RagLlm.ChatClient(sp),
                    sp.GetService<ILogger<LlmListwiseReranker>>()),
                "listwise");

            // The decision reranker follows the decision circuit (#433): off - the switch, the settings,
            // or no provider - it passes the passages through in their retrieval order, as noop does,
            // and says so once.
            var warned = 0;
            factory.Register(
                DecisionModelReranker.RerankerName,
                () =>
                {
                    var circuit = DecisionCircuit.Of(sp);
                    if (circuit.Enabled)
                        return new DecisionModelReranker(sp.GetRequiredService<IDecisionModel>());
                    if (Interlocked.Exchange(ref warned, 1) == 0)
                        RagRerankingLog.DecisionRerankerPassesThrough(
                            sp.GetService<ILogger<DecisionModelReranker>>() ?? (ILogger)Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                            circuit.OffReason!);
                    return new NoopReranker();
                });

            foreach (var registrar in sp.GetServices<IRerankerRegistrar>())
            {
                registrar.Register(factory, sp);
            }

            return factory;
        });

        return services;
    }
}

/// <summary>The warnings of the reranking stage.</summary>
internal static partial class RagRerankingLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The 'decision' reranker passes the passages through in their retrieval order: {Reason}.")]
    public static partial void DecisionRerankerPassesThrough(ILogger logger, string reason);
}
