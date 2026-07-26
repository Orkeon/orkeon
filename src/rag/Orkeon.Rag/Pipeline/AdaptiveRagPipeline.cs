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
/// <item><description><see cref="QueryRoute.Iterative"/> — <b>documented
/// fallback</b> to the iterative-fallback pipeline (<c>quality</c>): the real
/// iterative/corrective engine ships with RAG-06; until then the widest linear
/// preset is the honest stand-in, and the trace says so explicitly.</description></item>
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
    private readonly Func<IRagPipeline> _iterativeFallbackPipeline;
    private readonly string _singleShotProfileName;
    private readonly string _iterativeFallbackProfileName;
    private readonly ILogger<AdaptiveRagPipeline> _logger;

    /// <summary>Initializes the routing pipeline.</summary>
    /// <param name="classifier">Query-complexity classifier deciding the route.</param>
    /// <param name="chatClient">Chat client used for <see cref="QueryRoute.NoRetrieval"/> direct answers.</param>
    /// <param name="singleShotPipeline">Lazily resolves the <see cref="QueryRoute.SingleShot"/> delegate pipeline.</param>
    /// <param name="iterativeFallbackPipeline">Lazily resolves the <see cref="QueryRoute.Iterative"/> fallback pipeline (RAG-06 pending).</param>
    /// <param name="singleShotProfileName">Profile name of the single-shot delegate (tracing only).</param>
    /// <param name="iterativeFallbackProfileName">Profile name of the iterative fallback (tracing only).</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public AdaptiveRagPipeline(
        IQueryComplexityClassifier classifier,
        IChatClient chatClient,
        Func<IRagPipeline> singleShotPipeline,
        Func<IRagPipeline> iterativeFallbackPipeline,
        string singleShotProfileName = Abstractions.Options.RagProfilePresets.BalancedName,
        string iterativeFallbackProfileName = Abstractions.Options.RagProfilePresets.QualityName,
        ILogger<AdaptiveRagPipeline>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(singleShotPipeline);
        ArgumentNullException.ThrowIfNull(iterativeFallbackPipeline);
        ArgumentException.ThrowIfNullOrWhiteSpace(singleShotProfileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(iterativeFallbackProfileName);

        _classifier = classifier;
        _chatClient = chatClient;
        _singleShotPipeline = singleShotPipeline;
        _iterativeFallbackPipeline = iterativeFallbackPipeline;
        _singleShotProfileName = singleShotProfileName;
        _iterativeFallbackProfileName = iterativeFallbackProfileName;
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

            // RAG-06 pending: the iterative/corrective graph engine is not built
            // yet — Iterative routes fall back to the widest linear preset
            // (quality), and the trace documents the fallback explicitly.
            QueryRoute.Iterative => await DelegateAsync(
                _iterativeFallbackPipeline(),
                query,
                route,
                RouteStep(route, watch.Elapsed, _iterativeFallbackProfileName,
                    detail: $"iterative routing falls back to the '{_iterativeFallbackProfileName}' " +
                            "profile until the corrective engine ships (RAG-06)"),
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
