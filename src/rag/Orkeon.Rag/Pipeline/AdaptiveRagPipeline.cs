using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Pipeline;

/// <summary>
/// Adaptive-RAG routing pipeline (RAG-05/C3, guide §8.4) — the <c>adaptive</c>
/// profile: an <see cref="IQueryComplexityClassifier"/> routes each query first,
/// then the pipeline either answers directly or delegates to a preset pipeline:
/// <list type="bullet">
/// <item><description><see cref="QueryRoute.NoRetrieval"/> — direct LLM answer,
/// no retrieval, empty citations (traced).</description></item>
/// <item><description><see cref="QueryRoute.SingleShot"/> — delegates to the
/// single-shot pipeline (<c>balanced</c>).</description></item>
/// <item><description><see cref="QueryRoute.Iterative"/> — delegates to the
/// iterative pipeline: the <c>corrective</c> graph engine since RAG-06 (the
/// former documented fallback to <c>quality</c> is lifted).</description></item>
/// </list>
/// The routing decision is always traced: <see cref="RagTrace.Route"/> carries
/// the <see cref="QueryRoute"/>, and a <c>route</c> step (first step of the
/// trace) carries the classifier name, the route, and the delegated profile.
/// </summary>
public sealed partial class AdaptiveRagPipeline : IRagPipeline
{
    /// <summary>System prompt of the <see cref="QueryRoute.NoRetrieval"/> direct answer.</summary>
    public const string DirectAnswerSystemPrompt =
        "Answer the user directly and concisely. No knowledge-base retrieval was " +
        "performed for this query; do not fabricate citations.";

    private readonly IQueryComplexityClassifier _classifier;
    private readonly IChatClient _chatClient;
    private readonly Func<IRagPipeline> _singleShotPipeline;
    private readonly Func<IRagPipeline> _iterativePipeline;
    private readonly string _singleShotProfileName;
    private readonly string _iterativeProfileName;
    private readonly ILogger<AdaptiveRagPipeline> _logger;

    /// <summary>Initializes the routing pipeline.</summary>
    /// <param name="classifier">Query-complexity classifier deciding the route.</param>
    /// <param name="chatClient">Chat client used for <see cref="QueryRoute.NoRetrieval"/> direct answers.</param>
    /// <param name="singleShotPipeline">Lazily resolves the <see cref="QueryRoute.SingleShot"/> delegate pipeline.</param>
    /// <param name="iterativePipeline">Lazily resolves the <see cref="QueryRoute.Iterative"/> delegate pipeline (the corrective graph since RAG-06).</param>
    /// <param name="singleShotProfileName">Profile name of the single-shot delegate (tracing only).</param>
    /// <param name="iterativeProfileName">Profile name of the iterative delegate (tracing only).</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public AdaptiveRagPipeline(
        IQueryComplexityClassifier classifier,
        IChatClient chatClient,
        Func<IRagPipeline> singleShotPipeline,
        Func<IRagPipeline> iterativePipeline,
        string singleShotProfileName = Abstractions.Options.RagProfilePresets.BalancedName,
        string iterativeProfileName = Abstractions.Options.RagProfilePresets.CorrectiveName,
        ILogger<AdaptiveRagPipeline>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(singleShotPipeline);
        ArgumentNullException.ThrowIfNull(iterativePipeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(singleShotProfileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(iterativeProfileName);

        _classifier = classifier;
        _chatClient = chatClient;
        _singleShotPipeline = singleShotPipeline;
        _iterativePipeline = iterativePipeline;
        _singleShotProfileName = singleShotProfileName;
        _iterativeProfileName = iterativeProfileName;
        _logger = logger ?? NullLogger<AdaptiveRagPipeline>.Instance;
    }

    /// <inheritdoc />
    public async Task<RagAnswer> QueryAsync(
        RagQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Text);

        var watch = Stopwatch.StartNew();
        var route = await _classifier.ClassifyAsync(query.Text, cancellationToken).ConfigureAwait(false);
        watch.Stop();
        LogRouted(route, _classifier.GetType().Name);

        return route switch
        {
            QueryRoute.NoRetrieval => await AnswerDirectlyAsync(
                query, RouteStep(route, watch.Elapsed, delegateProfile: null), cancellationToken)
                .ConfigureAwait(false),

            QueryRoute.SingleShot => await DelegateAsync(
                _singleShotPipeline(),
                query,
                route,
                RouteStep(route, watch.Elapsed, _singleShotProfileName),
                cancellationToken).ConfigureAwait(false),

            // Since RAG-06 the Iterative route delegates to the corrective graph
            // engine — traced like any delegation (route step, delegate profile).
            QueryRoute.Iterative => await DelegateAsync(
                _iterativePipeline(),
                query,
                route,
                RouteStep(route, watch.Elapsed, _iterativeProfileName),
                cancellationToken).ConfigureAwait(false),

            _ => throw new InvalidOperationException($"Unknown query route '{route}'."),
        };
    }

    private async Task<RagAnswer> AnswerDirectlyAsync(
        RagQuery query,
        RagTraceStep routeStep,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, DirectAnswerSystemPrompt),
            new(ChatRole.User, query.Text),
        };
        var response = await _chatClient.GetResponseAsync(messages, options: null, cancellationToken)
            .ConfigureAwait(false);
        watch.Stop();

        var data = ImmutableDictionary<string, string>.Empty;
        if (!string.IsNullOrEmpty(response.ModelId))
            data = data.Add("model", response.ModelId);

        return new RagAnswer
        {
            Text = response.Text ?? string.Empty,
            Citations = ImmutableList<Citation>.Empty,
            Trace = new RagTrace
            {
                Route = QueryRoute.NoRetrieval,
                Steps =
                [
                    routeStep,
                    new RagTraceStep
                    {
                        Name = "generate",
                        Detail = "direct answer — no retrieval (route: NoRetrieval)",
                        Duration = watch.Elapsed,
                        Data = data,
                    },
                ],
            },
        };
    }

    private static async Task<RagAnswer> DelegateAsync(
        IRagPipeline pipeline,
        RagQuery query,
        QueryRoute route,
        RagTraceStep routeStep,
        CancellationToken cancellationToken)
    {
        var answer = await pipeline.QueryAsync(query, cancellationToken).ConfigureAwait(false);
        return answer with
        {
            Trace = answer.Trace with
            {
                Route = route,
                Steps = answer.Trace.Steps.Insert(0, routeStep),
            },
        };
    }

    private RagTraceStep RouteStep(
        QueryRoute route,
        TimeSpan duration,
        string? delegateProfile,
        string? detail = null)
    {
        var data = ImmutableDictionary<string, string>.Empty
            .Add("route", route.ToString())
            .Add("classifier", _classifier.GetType().Name);
        if (delegateProfile is not null)
            data = data.Add("delegate", delegateProfile);

        return new RagTraceStep
        {
            Name = "route",
            Detail = detail,
            Duration = duration,
            Data = data,
        };
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Adaptive-RAG routed the query to {Route} (classifier: {Classifier}).")]
    private partial void LogRouted(QueryRoute route, string classifier);
}
