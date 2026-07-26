using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Routing;

namespace Orkeon.Rag.DependencyInjection;

/// <summary>
/// Registers the Adaptive-RAG query-complexity classifier (RAG-05/C3, guide
/// §8.4): an <see cref="IQueryComplexityClassifier"/> selected by
/// <see cref="QueryRoutingOptions.Classifier"/> — <c>heuristic</c> (default,
/// deterministic, zero LLM) or <c>llm</c> (constrained lightweight chat call).
/// Called by <c>AddOrkeonRag</c>; safe to call directly and idempotent
/// (<c>TryAdd</c> — a host-registered classifier wins).
/// </summary>
public static partial class QueryRoutingExtensions
{
    /// <summary>Configuration section bound to <see cref="QueryRoutingOptions"/>.</summary>
    public const string QueryRoutingSectionKey = "Orkeon:Rag:QueryRouting";

    /// <summary>
    /// Binds <see cref="QueryRoutingOptions"/> and registers the singleton
    /// <see cref="IQueryComplexityClassifier"/> matching
    /// <see cref="QueryRoutingOptions.Classifier"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration root; <c>Orkeon:Rag:QueryRouting</c> is bound when present.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <c>llm</c> without a registered <see cref="IChatClient"/> falls back to the
    /// heuristic classifier with a warning (the documented no-LLM fallback); an
    /// unknown classifier name fails loudly with the list of supported names —
    /// never a silent fallback.
    /// </remarks>
    public static IServiceCollection AddOrkeonQueryRouting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions();
        services.Configure<QueryRoutingOptions>(configuration.GetSection(QueryRoutingSectionKey));

        services.TryAddSingleton<IQueryComplexityClassifier>(CreateClassifier);

        return services;
    }

    private static IQueryComplexityClassifier CreateClassifier(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<QueryRoutingOptions>>().Value;

        var name = string.IsNullOrWhiteSpace(options.Classifier)
            ? QueryRoutingOptions.HeuristicClassifier
#pragma warning disable CA1308 // lowercase is the option's alias form, not a comparison normalization
            : options.Classifier.Trim().ToLowerInvariant();
#pragma warning restore CA1308

        switch (name)
        {
            case QueryRoutingOptions.HeuristicClassifier:
                return new HeuristicQueryComplexityClassifier();

            case QueryRoutingOptions.LlmClassifier:
                var chatClient = serviceProvider.GetService<IChatClient>();
                if (chatClient is null)
                {
                    var logger = serviceProvider.GetService<ILogger<LlmQueryComplexityClassifier>>();
                    if (logger is not null)
                    {
                        LogLlmClassifierWithoutChatClient(logger);
                    }

                    return new HeuristicQueryComplexityClassifier();
                }

                return new LlmQueryComplexityClassifier(
                    chatClient,
                    serviceProvider.GetService<ILogger<LlmQueryComplexityClassifier>>());

            default:
                throw new InvalidOperationException(
                    $"Unknown query-complexity classifier '{options.Classifier}' (configuration key " +
                    $"'{QueryRoutingSectionKey}:Classifier'). Supported names: " +
                    $"{QueryRoutingOptions.HeuristicClassifier}, {QueryRoutingOptions.LlmClassifier}.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Query-routing classifier 'llm' is configured but no IChatClient is registered — " +
                  "falling back to the deterministic heuristic classifier.")]
    private static partial void LogLlmClassifierWithoutChatClient(ILogger logger);
}
