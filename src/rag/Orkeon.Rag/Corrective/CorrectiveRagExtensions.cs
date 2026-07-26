using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Configuration;

namespace Orkeon.Rag.Corrective;

/// <summary>
/// Registers the corrective RAG graph components (RAG-06/C1): the
/// <see cref="IRetrievalEvaluator"/>, the <see cref="IGroundednessChecker"/>
/// (first real implementation of the staged-pipeline hook), and the
/// <see cref="CorrectiveRagPipeline"/> itself. Safe to call alongside
/// <c>AddOrkeonRag</c> and idempotent (<c>TryAdd</c> — a host registration wins).
/// Since RAG-06/C2 <c>AddOrkeonRag</c> calls this method itself and the
/// <c>corrective</c> profile resolves through the profile resolver; the
/// concrete-type registration below remains for hosts wiring the graph alone.
/// </summary>
public static partial class CorrectiveRagExtensions
{
    /// <summary>
    /// Adds the corrective RAG services: an <see cref="IRetrievalEvaluator"/>
    /// and an <see cref="IGroundednessChecker"/> (LLM-backed when an
    /// <see cref="IChatClient"/> is registered, deterministic heuristics with a
    /// warning otherwise), and the <see cref="CorrectiveRagPipeline"/> singleton.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration root; <c>Orkeon:Rag</c> (incl. <c>Corrective</c>) is bound when present.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// The optional <see cref="IWebDocumentRetriever"/> is NOT registered here:
    /// the <c>web_fallback</c> node stays skipped (and traced) until the host
    /// registers one — <c>AddOrkeonRagWebFallback</c> does when the transport is
    /// enabled and configured — AND enables
    /// <c>Orkeon:Rag:Corrective:WebFallback:Enabled</c>.
    /// </remarks>
    public static IServiceCollection AddOrkeonCorrectiveRag(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions();

        // Same effective-options source as AddOrkeonRag (profile preset +
        // Orkeon:Rag overrides); TryAdd keeps whichever registered first.
        services.TryAddSingleton(_ => RagOptionsFactory.Build(configuration));

        services.TryAddSingleton<IRetrievalEvaluator>(CreateRetrievalEvaluator);
        services.TryAddSingleton<IGroundednessChecker>(CreateGroundednessChecker);

        services.TryAddSingleton(serviceProvider => new CorrectiveRagPipeline(
            serviceProvider.GetRequiredService<IDocumentStore>(),
            serviceProvider.GetRequiredService<IEmbeddingProvider>(),
            serviceProvider.GetRequiredService<IChatClient>(),
            serviceProvider.GetRequiredService<IRetrievalEvaluator>(),
            serviceProvider.GetRequiredService<RagOptions>(),
            serviceProvider.GetService<IGroundednessChecker>(),
            serviceProvider.GetService<IWebDocumentRetriever>(),
            serviceProvider.GetService<ILogger<CorrectiveRagPipeline>>()));

        return services;
    }

    private static IRetrievalEvaluator CreateRetrievalEvaluator(IServiceProvider serviceProvider)
    {
        var chatClient = serviceProvider.GetService<IChatClient>();
        if (chatClient is null)
        {
            var logger = serviceProvider.GetService<ILogger<LlmRetrievalEvaluator>>();
            if (logger is not null)
                LogHeuristicEvaluatorFallback(logger);

            return new HeuristicRetrievalEvaluator();
        }

        return new LlmRetrievalEvaluator(
            chatClient,
            serviceProvider.GetService<ILogger<LlmRetrievalEvaluator>>());
    }

    private static IGroundednessChecker CreateGroundednessChecker(IServiceProvider serviceProvider)
    {
        var chatClient = serviceProvider.GetService<IChatClient>();
        if (chatClient is null)
        {
            var logger = serviceProvider.GetService<ILogger<LlmGroundednessChecker>>();
            if (logger is not null)
                LogHeuristicCheckerFallback(logger);

            return new HeuristicGroundednessChecker();
        }

        return new LlmGroundednessChecker(
            chatClient,
            serviceProvider.GetService<ILogger<LlmGroundednessChecker>>());
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No IChatClient is registered — the retrieval evaluator falls back to the " +
                  "deterministic lexical heuristic (offline mode).")]
    private static partial void LogHeuristicEvaluatorFallback(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No IChatClient is registered — the groundedness checker falls back to the " +
                  "deterministic lexical heuristic (offline mode).")]
    private static partial void LogHeuristicCheckerFallback(ILogger logger);
}
