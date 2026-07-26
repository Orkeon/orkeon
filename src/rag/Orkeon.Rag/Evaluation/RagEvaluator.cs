using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Evaluation;

/// <summary>
/// Default <see cref="IRagEvaluator"/> (plan §9): queries the pipeline resolved
/// for the requested profile once per case, computes the deterministic retrieval
/// metrics from the citations, and judges the generation with the LLM judge when
/// requested AND available — the deterministic heuristic otherwise. The judge
/// mode is ALWAYS labelled, per case and per report.
/// </summary>
public sealed partial class RagEvaluator : IRagEvaluator
{
    private readonly IRagProfileResolver _profileResolver;
    private readonly IChatClient? _judgeChatClient;
    private readonly ILogger<RagEvaluator> _logger;

    /// <summary>Initializes the evaluator.</summary>
    /// <param name="profileResolver">Profile-name → pipeline resolution seam (RAG-04/C4 hook).</param>
    /// <param name="judgeChatClient">
    /// Optional chat client for the LLM judge. <c>null</c> forces the heuristic
    /// judge even when <see cref="RagEvalOptions.UseLlmJudge"/> is set.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public RagEvaluator(
        IRagProfileResolver profileResolver,
        IChatClient? judgeChatClient = null,
        ILogger<RagEvaluator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(profileResolver);
        _profileResolver = profileResolver;
        _judgeChatClient = judgeChatClient;
        _logger = logger ?? NullLogger<RagEvaluator>.Instance;
    }

    /// <inheritdoc />
    public async Task<RagEvalReport> RunAsync(
        RagEvalDataset dataset,
        RagEvalOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Collection);
        if (dataset.Cases.Count == 0)
            throw new ArgumentException("The dataset contains no case.", nameof(dataset));

        var pipeline = _profileResolver.Resolve(options.Profile);
        var judge = SelectJudge(options);
        var startedAt = DateTimeOffset.UtcNow;
        var runWatch = Stopwatch.StartNew();
        var results = ImmutableList.CreateBuilder<RagEvalCaseResult>();

        foreach (var evalCase in dataset.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await EvaluateCaseAsync(pipeline, judge, evalCase, options, cancellationToken)
                .ConfigureAwait(false));
        }

        runWatch.Stop();
        var cases = results.ToImmutable();
        var fallbackCount = judge.Mode == RagJudgeMode.Llm
            ? cases.Count(c => c.Judge == RagJudgeMode.Heuristic)
            : 0;

        LogRunCompleted(dataset.Name, options.Profile, cases.Count, judge.Mode, fallbackCount);

        return new RagEvalReport
        {
            DatasetName = dataset.Name,
            Profile = options.Profile,
            Collection = options.Collection,
            K = options.K,
            Judge = judge.Mode,
            JudgeFallbackCount = fallbackCount,
            Cases = cases,
            Aggregate = Aggregate(cases),
            StartedAt = startedAt,
            Duration = runWatch.Elapsed,
        };
    }

    private IGenerationJudge SelectJudge(RagEvalOptions options)
    {
        if (options.UseLlmJudge && _judgeChatClient is not null)
            return new LlmGenerationJudge(_judgeChatClient);

        return new HeuristicGenerationJudge();
    }

    private static async Task<RagEvalCaseResult> EvaluateCaseAsync(
        IRagPipeline pipeline,
        IGenerationJudge judge,
        RagEvalCase evalCase,
        RagEvalOptions options,
        CancellationToken cancellationToken)
    {
        var caseWatch = Stopwatch.StartNew();
        var answer = await pipeline.QueryAsync(
            new RagQuery
            {
                Text = evalCase.Question,
                Collection = options.Collection,
                TopN = Math.Max(options.TopN, options.K),
            },
            cancellationToken).ConfigureAwait(false);
        caseWatch.Stop();

        var rankedKeys = answer.Citations
            .OrderBy(c => c.Marker)
            .Select(CitationKeys)
            .ToList();

        double? recall = null, precision = null, reciprocalRank = null;
        if (evalCase.Relevant.Count > 0)
        {
            recall = RetrievalEvalMetrics.RecallAtK(evalCase.Relevant, rankedKeys, options.K);
            precision = RetrievalEvalMetrics.PrecisionAtK(evalCase.Relevant, rankedKeys, options.K);
            reciprocalRank = RetrievalEvalMetrics.ReciprocalRank(evalCase.Relevant, rankedKeys);
        }

        var judgement = await judge.JudgeAsync(evalCase, answer, cancellationToken).ConfigureAwait(false);

        return new RagEvalCaseResult
        {
            CaseId = evalCase.Id,
            Tags = evalCase.Tags,
            RetrievedSources = [.. answer.Citations.OrderBy(c => c.Marker).Select(c => c.SourceId)],
            RecallAtK = recall,
            PrecisionAtK = precision,
            ReciprocalRank = reciprocalRank,
            Groundedness = judgement.Groundedness,
            AnswerRelevance = judgement.AnswerRelevance,
            Judge = judgement.Mode,
            Answer = answer.Text,
            Duration = caseWatch.Elapsed,
        };
    }

    private static IReadOnlyList<string> CitationKeys(Citation citation)
    {
        var keys = new List<string>(3) { citation.ChunkId, citation.SourceId };
        if (!string.IsNullOrWhiteSpace(citation.DocumentId))
            keys.Add(citation.DocumentId);
        return keys;
    }

    private static RagEvalAggregate Aggregate(ImmutableList<RagEvalCaseResult> cases)
        => new()
        {
            CaseCount = cases.Count,
            RecallAtK = MeanOf(cases, c => c.RecallAtK),
            PrecisionAtK = MeanOf(cases, c => c.PrecisionAtK),
            Mrr = MeanOf(cases, c => c.ReciprocalRank),
            Groundedness = MeanOf(cases, c => c.Groundedness),
            AnswerRelevance = MeanOf(cases, c => c.AnswerRelevance),
        };

    private static double? MeanOf(
        IReadOnlyList<RagEvalCaseResult> cases,
        Func<RagEvalCaseResult, double?> selector)
    {
        var values = cases.Select(selector).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return values.Count > 0 ? values.Average() : null;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "RAG eval run completed — dataset '{Dataset}', profile '{Profile}', {CaseCount} case(s), judge: {Judge} ({FallbackCount} heuristic fallback(s)).")]
    private partial void LogRunCompleted(string dataset, string profile, int caseCount, RagJudgeMode judge, int fallbackCount);
}
