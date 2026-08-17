using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Constants.Rag;
using Orkeon.Domain.Graph;
using Orkeon.Rag.Abstractions;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Pipeline;
using Orkeon.Rag.Retrieval;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Rag.Corrective;

/// <summary>
/// Corrective RAG pipeline (CRAG, RAG-06/C1, guide §8) — <em>built on Orkeon's
/// Graph orchestration mode</em>: an <see cref="IRagPipeline"/> whose execution
/// is a <see cref="StateGraph{TState}"/> over <see cref="RagGraphState"/> with
/// conditional edges and controlled cycles:
/// <c>retrieve → evaluate</c>, then per <see cref="RetrievalGrade"/> —
/// <c>Correct → generate</c>; <c>Incorrect → rewrite_query</c> (vocabulary-gap
/// rewrite, loop back to <c>retrieve</c>); <c>Ambiguous → refine</c>
/// (decompose-then-recompose) then <c>generate</c>. After rewriting is
/// exhausted, the opt-in <c>web_fallback</c> node fires (only when enabled AND
/// an <see cref="IWebDocumentRetriever"/> is registered; skipped and traced
/// otherwise). <c>generate</c> answers the ORIGINAL user question with the same
/// <c>[n]</c> citation markers as the staged pipeline; <c>check_groundedness</c>
/// re-loops to <c>rewrite_query</c> on an ungrounded answer.
/// </summary>
/// <remarks>
/// <para><b>Guard rails</b> — the corrective loop is bounded twice: by
/// <see cref="RagCorrectiveOptions.MaxIterations"/> (routing never re-enters
/// <c>rewrite_query</c> past the budget), and by the circuit breaker native to
/// the graph engine (<see cref="CircuitBreakerPolicy"/>, configured explicitly
/// from the iteration budget). At exhaustion the pipeline generates with the
/// best chunks available and traces it; a tripped breaker is caught, traced
/// (<c>corrective:circuit_breaker</c>) and degraded to a best-effort answer —
/// the pipeline never throws for a loop condition and can never loop forever.</para>
/// <para><b>Tracing</b> — every node run appends a
/// <c>corrective:&lt;node&gt;</c> step (with the iteration ordinal) to
/// <see cref="RagAnswer.Trace"/>; verdicts land in <see cref="RagTrace.Verdicts"/>,
/// rewritten probes in <see cref="RagTrace.QueryVariants"/>, and the loop count
/// in <see cref="RagTrace.Iterations"/>.</para>
/// </remarks>
[Experimental("ORKEXP003", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed partial class CorrectiveRagPipeline : IRagPipeline
{
    /// <summary>Graph node retrieving candidates for the current probe.</summary>
    public const string RetrieveNode = "retrieve";

    /// <summary>Graph node grading the retrieved chunks (<see cref="IRetrievalEvaluator"/>).</summary>
    public const string EvaluateNode = "evaluate";

    /// <summary>Graph node rewriting the retrieval probe (vocabulary gap), looping back to <see cref="RetrieveNode"/>.</summary>
    public const string RewriteQueryNode = "rewrite_query";

    /// <summary>Graph node refining ambiguous chunks (decompose-then-recompose).</summary>
    public const string RefineNode = "refine";

    /// <summary>Opt-in graph node fetching web documents after rewriting is exhausted.</summary>
    public const string WebFallbackNode = "web_fallback";

    /// <summary>Graph node generating the cited answer to the ORIGINAL question.</summary>
    public const string GenerateNode = "generate";

    /// <summary>Graph node verifying the answer against the context (<see cref="IGroundednessChecker"/>).</summary>
    public const string CheckGroundednessNode = "check_groundedness";

    /// <summary>Prefix of every trace step emitted by this pipeline.</summary>
    public const string TraceStepPrefix = "corrective:";

    /// <summary>System prompt of the <see cref="RewriteQueryNode"/> LLM call.</summary>
    public const string RewriteSystemPrompt =
        "You rewrite search queries for a retrieval system. The previous query failed to retrieve " +
        "relevant passages, most likely because of a vocabulary gap between the question and the " +
        "documents. Rewrite the query with alternative wording, synonyms, and expanded key terms. " +
        "Respond with ONLY the rewritten query text — a single line, no quotes, no explanation.";

    /// <summary><see cref="ScoredChunk.ScoreOrigin"/> of chunks produced by the web fallback.</summary>
    public const string WebScoreOrigin = "web";

    private const int CitationSnippetLength = 200;
    private const int TraceTextLength = 160;
    private const double RefineRelevanceThreshold = 0.5;

    private readonly IDocumentStore _store;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IChatClient _chatClient;
    private readonly IRetrievalEvaluator _retrievalEvaluator;
    private readonly IGroundednessChecker? _groundednessChecker;
    private readonly IWebDocumentRetriever? _webRetriever;
    private readonly RagOptions _options;
    private readonly CircuitBreakerPolicy _circuitPolicy;
    private readonly ILogger<CorrectiveRagPipeline> _logger;
    private readonly bool _edgesOrdering;
    private readonly int _maxIterations;

    /// <summary>Initializes the corrective pipeline.</summary>
    /// <param name="store">Document store queried at retrieval.</param>
    /// <param name="embeddingProvider">Application embedding port used to embed the retrieval probes.</param>
    /// <param name="chatClient">Chat client used for query rewriting and grounded generation.</param>
    /// <param name="retrievalEvaluator">CRAG retrieval evaluator grading each retrieval pass.</param>
    /// <param name="options">Pipeline options; <c>null</c> selects the defaults.</param>
    /// <param name="groundednessChecker">
    /// Optional groundedness checker closing the loop; absent, the
    /// <c>check_groundedness</c> node is traced as skipped and the graph ends.
    /// </param>
    /// <param name="webRetriever">
    /// Optional web document retriever for the opt-in <c>web_fallback</c> node
    /// (requires <see cref="RagWebFallbackOptions.Enabled"/> too; the edge is
    /// skipped and traced otherwise).
    /// </param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <param name="circuitPolicy">
    /// Explicit circuit-breaker override for the graph engine; <c>null</c>
    /// derives a policy from <see cref="RagCorrectiveOptions.MaxIterations"/>
    /// (see <see cref="BuildCircuitPolicy"/>).
    /// </param>
    public CorrectiveRagPipeline(
        IDocumentStore store,
        IEmbeddingProvider embeddingProvider,
        IChatClient chatClient,
        IRetrievalEvaluator retrievalEvaluator,
        RagOptions? options = null,
        IGroundednessChecker? groundednessChecker = null,
        IWebDocumentRetriever? webRetriever = null,
        ILogger<CorrectiveRagPipeline>? logger = null,
        CircuitBreakerPolicy? circuitPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(embeddingProvider);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(retrievalEvaluator);

        _store = store;
        _embeddingProvider = embeddingProvider;
        _chatClient = chatClient;
        _retrievalEvaluator = retrievalEvaluator;
        _groundednessChecker = groundednessChecker;
        _webRetriever = webRetriever;
        _options = options ?? new RagOptions();
        _logger = logger ?? NullLogger<CorrectiveRagPipeline>.Instance;

        _maxIterations = _options.Corrective.MaxIterations;
        ArgumentOutOfRangeException.ThrowIfNegative(_maxIterations);
        _circuitPolicy = circuitPolicy ?? BuildCircuitPolicy(_maxIterations);
        _edgesOrdering = IsEdgesOrdering(_options.Context.Ordering);
    }

    /// <summary>The effective options of this pipeline instance.</summary>
    public RagOptions Options => _options;

    /// <summary>
    /// Derives the graph circuit-breaker policy from the corrective iteration
    /// budget: the loop nodes may be visited at most <c>maxIterations + 1</c>
    /// times, so the breaker (second layer of protection, explicitly configured)
    /// trips only if the routing invariants are ever violated.
    /// </summary>
    internal static CircuitBreakerPolicy BuildCircuitPolicy(int maxIterations) => new()
    {
        MaxTransitions = (maxIterations + 2) * 7,
        MaxStateVisits = maxIterations + 2,
        StateTimeout = TimeSpan.FromMinutes(2),
        MaxTotalDuration = TimeSpan.FromMinutes(10),
    };

    /// <inheritdoc />
    public async Task<RagAnswer> QueryAsync(
        RagQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Collection);

        var initialState = new RagGraphState { Query = query };
        var runner = BuildGraph().Compile();

        // Last state observed before a potential circuit break — the breaker
        // degrades to a best-effort answer, never to an exception.
        var lastState = initialState;
        runner.OnNodeCompleted += (_, e) => lastState = e.State;

        RagGraphState state;
        try
        {
            var result = await runner.RunAsync(initialState, cancellationToken).ConfigureAwait(false);
            state = result.FinalState;
        }
        catch (GraphCircuitBrokenException broken)
        {
            LogCircuitBroken(broken.Message);
            state = lastState with
            {
                Exhausted = true,
                Steps = lastState.Steps.Add(new RagTraceStep
                {
                    Name = TraceStepPrefix + "circuit_breaker",
                    Detail = broken.Message + " — degrading to a best-effort answer with the best available chunks",
                    Data = ImmutableDictionary<string, string>.Empty
                        .Add("transitions", broken.TransitionCount.ToString(CultureInfo.InvariantCulture))
                        .Add("node", broken.NodeName),
                }),
            };

            // The breaker may trip before generation ran — generate directly
            // (outside the graph) with whatever chunks the loop had retained.
            if (state.Answer is null)
                state = await GenerateAsync(state, cancellationToken).ConfigureAwait(false);
        }

        return Compose(state);
    }

    // ── graph topology ─────────────────────────────────────────────────────

    private StateGraph<RagGraphState> BuildGraph()
    {
        var graph = new StateGraph<RagGraphState>(_circuitPolicy)
            .AddNode(RetrieveNode, RetrieveAsync)
            .AddNode(EvaluateNode, EvaluateAsync)
            .AddNode(RewriteQueryNode, RewriteQueryAsync)
            .AddNode(RefineNode, RefineAsync)
            .AddNode(WebFallbackNode, WebFallbackAsync)
            .AddNode(GenerateNode, GenerateAsync)
            .AddNode(CheckGroundednessNode, CheckGroundednessAsync);

        graph.AddEdge(StateGraph<RagGraphState>.StartNode, RetrieveNode);
        graph.AddEdge(RetrieveNode, EvaluateNode);
        graph.AddConditionalEdge(
            EvaluateNode,
            state => state.NextNode,
            [GenerateNode, RewriteQueryNode, RefineNode, WebFallbackNode]);
        graph.AddEdge(RewriteQueryNode, RetrieveNode);
        graph.AddEdge(RefineNode, GenerateNode);
        graph.AddEdge(WebFallbackNode, GenerateNode);
        graph.AddEdge(GenerateNode, CheckGroundednessNode);
        graph.AddConditionalEdge(
            CheckGroundednessNode,
            state => state.NextNode,
            [RewriteQueryNode, StateGraph<RagGraphState>.EndNode]);

        return graph;
    }

    // ── nodes ──────────────────────────────────────────────────────────────

    private async Task<RagGraphState> RetrieveAsync(
        RagGraphState state,
        CancellationToken cancellationToken)
    {
        var topN = Math.Max(1, state.Query.TopN);
        var width = Math.Max(_options.Retrieval.CandidateK, topN);
        var hybrid = _options.Retrieval.Hybrid.Enabled;
        var probe = state.CurrentQueryText;

        var watch = Stopwatch.StartNew();
        var embedding = await _embeddingProvider.GetEmbeddingAsync(probe, cancellationToken)
            .ConfigureAwait(false);

        var hits = await _store.SearchAsync(
                state.Query.Collection,
                new RetrievalQuery
                {
                    Text = probe,
                    Embedding = embedding.Length > 0 ? [.. embedding] : null,
                    TopK = width,
                    Filters = state.Query.Filters,
                    Hybrid = hybrid,
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (_options.Retrieval.MinScore is { } floor)
            hits = [.. hits.Where(hit => hit.Score >= floor)];

        var kept = hits.Take(topN).ToImmutableList();
        watch.Stop();

        return state with
        {
            Chunks = kept,
            Verdict = null,
            Steps = state.Steps.Add(new RagTraceStep
            {
                Name = TraceStepPrefix + RetrieveNode,
                Duration = watch.Elapsed,
                Data = ImmutableDictionary<string, string>.Empty
                    .Add("iteration", state.Iteration.ToString(CultureInfo.InvariantCulture))
                    .Add("probe", Truncate(probe))
                    .Add("candidates", hits.Count.ToString(CultureInfo.InvariantCulture))
                    .Add("kept", kept.Count.ToString(CultureInfo.InvariantCulture))
                    .Add("hybrid", hybrid ? "true" : "false"),
            }),
        };
    }

    private async Task<RagGraphState> EvaluateAsync(
        RagGraphState state,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();

        // The verdict grades relevance to the ORIGINAL user question — the
        // probe may have been rewritten, the information need has not.
        var verdict = await _retrievalEvaluator
            .EvaluateAsync(state.Query.Text, state.Chunks, cancellationToken)
            .ConfigureAwait(false);
        watch.Stop();

        var (next, exhausted, detail) = DecideAfterEvaluate(state, verdict);

        var data = ImmutableDictionary<string, string>.Empty
            .Add("iteration", state.Iteration.ToString(CultureInfo.InvariantCulture))
            .Add("grade", verdict.Grade.ToString())
            .Add("evaluator", _retrievalEvaluator.GetType().Name)
            .Add("next", next);
        if (!string.IsNullOrWhiteSpace(verdict.Rationale))
            data = data.Add("reason", Truncate(verdict.Rationale));

        return state with
        {
            Verdict = verdict,
            Verdicts = state.Verdicts.Add(verdict),
            NextNode = next,
            Exhausted = state.Exhausted || exhausted,
            Steps = state.Steps.Add(new RagTraceStep
            {
                Name = TraceStepPrefix + EvaluateNode,
                Detail = detail,
                Duration = watch.Elapsed,
                Data = data,
            }),
        };
    }

    /// <summary>
    /// Routing decision after <see cref="EvaluateNode"/>: <c>Correct</c> →
    /// generate; <c>Ambiguous</c> → refine; <c>Incorrect</c> → rewrite while the
    /// iteration budget lasts, then the opt-in web fallback (enabled AND
    /// registered AND not yet attempted), else best-effort generation — the skip
    /// is traced explicitly.
    /// </summary>
    private (string Next, bool Exhausted, string? Detail) DecideAfterEvaluate(
        RagGraphState state,
        RetrievalVerdict verdict)
    {
        switch (verdict.Grade)
        {
            case RetrievalGrade.Correct:
                return (GenerateNode, false, null);

            case RetrievalGrade.Ambiguous:
                return (RefineNode, false, "ambiguous retrieval — refining (decompose-then-recompose)");

            // RetrievalGrade.Incorrect lands here too — it is the default corrective path.
            default:
                if (state.Iteration < _maxIterations)
                    return (RewriteQueryNode, false, "incorrect retrieval — rewriting the query (vocabulary gap)");

                var budget = string.Create(
                    CultureInfo.InvariantCulture,
                    $"iteration budget exhausted ({state.Iteration}/{_maxIterations})");

                if (!_options.Corrective.WebFallback.Enabled)
                {
                    return (GenerateNode, true,
                        budget + " — web_fallback skipped (disabled) — generating with the best available chunks");
                }

                if (_webRetriever is null)
                {
                    return (GenerateNode, true,
                        budget + " — web_fallback skipped (no IWebDocumentRetriever registered) — " +
                        "generating with the best available chunks");
                }

                if (state.WebFallbackAttempted)
                {
                    return (GenerateNode, true,
                        budget + " — web_fallback already attempted — generating with the best available chunks");
                }

                return (WebFallbackNode, true, budget + " — trying the web fallback");
        }
    }

    private async Task<RagGraphState> RewriteQueryAsync(
        RagGraphState state,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder()
            .Append("Original question: ").AppendLine(state.Query.Text)
            .Append("Previous query: ").AppendLine(state.CurrentQueryText);
        if (!string.IsNullOrWhiteSpace(state.Verdict?.Rationale))
            builder.Append("Evaluator feedback: ").AppendLine(state.Verdict.Rationale);
        if (state.Groundedness is { IsGrounded: false, UnsupportedClaims.Count: > 0 } groundedness)
            builder.Append("Unsupported claims to cover: ").AppendLine(string.Join("; ", groundedness.UnsupportedClaims));

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, RewriteSystemPrompt),
            new(ChatRole.User, builder.ToString()),
        };

        var watch = Stopwatch.StartNew();
        var response = await _chatClient
            .GetResponseAsync(messages, new ChatOptions { Temperature = 0f }, cancellationToken)
            .ConfigureAwait(false);
        watch.Stop();

        var rewritten = CleanRewrittenQuery(response.Text);
        var iteration = state.Iteration + 1;

        string? detail = null;
        if (rewritten is null)
        {
            // A rewrite that produces nothing usable never fails the run: the
            // previous probe is retried and the iteration budget still advances.
            detail = "rewrite produced no usable text — retrying with the previous probe";
            LogUnusableRewrite();
        }

        var probe = rewritten ?? state.CurrentQueryText;
        return state with
        {
            RewrittenQuery = probe,
            RewrittenQueries = rewritten is null ? state.RewrittenQueries : state.RewrittenQueries.Add(rewritten),
            Iteration = iteration,
            Steps = state.Steps.Add(new RagTraceStep
            {
                Name = TraceStepPrefix + RewriteQueryNode,
                Detail = detail,
                Duration = watch.Elapsed,
                Data = ImmutableDictionary<string, string>.Empty
                    .Add("iteration", iteration.ToString(CultureInfo.InvariantCulture))
                    .Add("rewritten", Truncate(probe)),
            }),
        };
    }

    /// <summary>
    /// Decompose-then-recompose refinement of an ambiguous retrieval: chunks are
    /// filtered by the evaluator's per-chunk relevances (when provided), then
    /// each kept chunk is split into sentences and only the segments sharing
    /// vocabulary with the question are recomposed. Never empties the working
    /// set: an over-aggressive filter falls back to the unrefined chunks.
    /// </summary>
    private Task<RagGraphState> RefineAsync(
        RagGraphState state,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();

        // 1 — filter by the evaluator's per-chunk relevances, when provided.
        var relevances = state.Verdict?.ChunkRelevances;
        IReadOnlyList<ScoredChunk> filtered = state.Chunks;
        if (relevances is { Count: > 0 })
        {
            var byId = relevances.ToDictionary(r => r.ChunkId, r => r.Relevance, StringComparer.Ordinal);
            var kept = state.Chunks
                .Where(scored => !byId.TryGetValue(scored.Chunk.Id, out var relevance)
                    || relevance >= RefineRelevanceThreshold)
                .ToList();
            if (kept.Count > 0)
                filtered = kept;
        }

        // 2 — re-split: keep the segments sharing vocabulary with the question.
        var queryTokens = Bm25Index.Tokenize(state.Query.Text).ToHashSet(StringComparer.Ordinal);
        var refined = new List<ScoredChunk>(filtered.Count);
        var segmentsKept = 0;
        var segmentsTotal = 0;
        foreach (var scored in filtered)
        {
            var sentences = HeuristicGroundednessChecker.SplitSentences(scored.Chunk.Content);
            segmentsTotal += sentences.Count;
            var relevant = queryTokens.Count == 0
                ? sentences
                : sentences.Where(s => Bm25Index.Tokenize(s).Any(queryTokens.Contains)).ToList();

            if (relevant.Count == 0)
                continue; // no relevant segment — the chunk is dropped

            segmentsKept += relevant.Count;
            var content = relevant.Count == sentences.Count
                ? scored.Chunk.Content
                : string.Join(Environment.NewLine, relevant);
            refined.Add(scored with { Chunk = scored.Chunk with { Content = content } });
        }

        // Never empty the working set — refinement narrows, it must not blind.
        var result = refined.Count > 0 ? refined : [.. filtered];
        watch.Stop();

        return Task.FromResult(state with
        {
            Chunks = [.. result],
            Steps = state.Steps.Add(new RagTraceStep
            {
                Name = TraceStepPrefix + RefineNode,
                Duration = watch.Elapsed,
                Data = ImmutableDictionary<string, string>.Empty
                    .Add("iteration", state.Iteration.ToString(CultureInfo.InvariantCulture))
                    .Add("in", state.Chunks.Count.ToString(CultureInfo.InvariantCulture))
                    .Add("out", result.Count.ToString(CultureInfo.InvariantCulture))
                    .Add("segments_kept", segmentsKept.ToString(CultureInfo.InvariantCulture))
                    .Add("segments_total", segmentsTotal.ToString(CultureInfo.InvariantCulture)),
            }),
        });
    }

    private async Task<RagGraphState> WebFallbackAsync(
        RagGraphState state,
        CancellationToken cancellationToken)
    {
        var maxResults = Math.Max(1, _options.Corrective.WebFallback.MaxResults);
        var watch = Stopwatch.StartNew();
        var documents = await _webRetriever!
            .SearchAsync(state.CurrentQueryText, maxResults, cancellationToken)
            .ConfigureAwait(false);
        watch.Stop();

        var topN = Math.Max(1, state.Query.TopN);
        var webChunks = documents
            .Select((document, index) => new ScoredChunk
            {
                Chunk = new Chunk
                {
                    Id = "web:" + document.Id,
                    DocumentId = document.Id,
                    SourceId = document.SourceId,
                    Content = document.Content,
                    Metadata = document.Metadata,
                },
                Score = 1.0 / (1 + index),
                ScoreOrigin = WebScoreOrigin,
            })
            .ToList();

        // Web results open the working set (they are the corrective addition)  —
        // the locally retrieved chunks — graded Incorrect — close it.
        var merged = webChunks.Concat(state.Chunks).Take(topN).ToImmutableList();

        return state with
        {
            Chunks = webChunks.Count > 0 ? merged : state.Chunks,
            WebFallbackAttempted = true,
            Steps = state.Steps.Add(new RagTraceStep
            {
                Name = TraceStepPrefix + WebFallbackNode,
                Detail = webChunks.Count == 0 ? "no web result — generating with the best available chunks" : null,
                Duration = watch.Elapsed,
                Data = ImmutableDictionary<string, string>.Empty
                    .Add("iteration", state.Iteration.ToString(CultureInfo.InvariantCulture))
                    .Add("results", webChunks.Count.ToString(CultureInfo.InvariantCulture))
                    .Add("max_results", maxResults.ToString(CultureInfo.InvariantCulture)),
            }),
        };
    }

    private async Task<RagGraphState> GenerateAsync(
        RagGraphState state,
        CancellationToken cancellationToken)
    {
        if (state.Chunks.Count == 0)
        {
            LogNoContextRetrieved(state.Query.Collection);
            return state with
            {
                Answer = StagedRagPipeline.NoContextAnswer,
                Steps = state.Steps.Add(new RagTraceStep
                {
                    Name = TraceStepPrefix + GenerateNode,
                    Detail = "skipped — no retrieved context",
                    Data = ImmutableDictionary<string, string>.Empty
                        .Add("iteration", state.Iteration.ToString(CultureInfo.InvariantCulture)),
                }),
            };
        }

        var watch = Stopwatch.StartNew();
        var (contextBlock, kept) = AssembleContext(state.Chunks);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, _options.Generation.SystemPrompt ?? StagedRagPipeline.DefaultSystemPrompt),
            // The ORIGINAL user question — never the rewritten probe.
            new(ChatRole.User, $"Context:\n{contextBlock}\n\nQuestion: {state.Query.Text}"),
        };

        var chatOptions = new ChatOptions
        {
            Temperature = _options.Generation.Temperature,
            MaxOutputTokens = _options.Generation.MaxOutputTokens,
        };

        var response = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken)
            .ConfigureAwait(false);
        watch.Stop();

        var data = ImmutableDictionary<string, string>.Empty
            .Add("iteration", state.Iteration.ToString(CultureInfo.InvariantCulture))
            .Add("chunks", kept.Count.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(response.ModelId))
            data = data.Add("model", response.ModelId);
        if (state.Exhausted)
            data = data.Add("best_effort", "true");

        return state with
        {
            Answer = response.Text ?? string.Empty,
            Chunks = kept,
            Steps = state.Steps.Add(new RagTraceStep
            {
                Name = TraceStepPrefix + GenerateNode,
                Detail = state.Exhausted ? "best-effort generation — corrective budget exhausted" : null,
                Duration = watch.Elapsed,
                Data = data,
            }),
        };
    }

    private async Task<RagGraphState> CheckGroundednessAsync(
        RagGraphState state,
        CancellationToken cancellationToken)
    {
        var iterationData = ImmutableDictionary<string, string>.Empty
            .Add("iteration", state.Iteration.ToString(CultureInfo.InvariantCulture));

        if (string.Equals(state.Answer, StagedRagPipeline.NoContextAnswer, StringComparison.Ordinal))
        {
            return state with
            {
                NextNode = StateGraph<RagGraphState>.EndNode,
                Steps = state.Steps.Add(new RagTraceStep
                {
                    Name = TraceStepPrefix + CheckGroundednessNode,
                    Detail = "skipped — deterministic no-context answer",
                    Data = iterationData,
                }),
            };
        }

        if (_groundednessChecker is null)
        {
            return state with
            {
                NextNode = StateGraph<RagGraphState>.EndNode,
                Steps = state.Steps.Add(new RagTraceStep
                {
                    Name = TraceStepPrefix + CheckGroundednessNode,
                    Detail = "skipped — no IGroundednessChecker registered",
                    Data = iterationData,
                }),
            };
        }

        var watch = Stopwatch.StartNew();
        var result = await _groundednessChecker
            .CheckAsync(state.Query.Text, state.Answer ?? string.Empty, state.Chunks, cancellationToken)
            .ConfigureAwait(false);
        watch.Stop();

        string next;
        var exhausted = state.Exhausted;
        string? detail = null;
        if (result.IsGrounded)
        {
            next = StateGraph<RagGraphState>.EndNode;
        }
        else if (state.Iteration < _maxIterations && !state.Exhausted)
        {
            next = RewriteQueryNode;
            detail = "ungrounded answer — re-looping through rewrite_query";
        }
        else
        {
            next = StateGraph<RagGraphState>.EndNode;
            exhausted = true;
            detail = string.Create(
                CultureInfo.InvariantCulture,
                $"ungrounded answer but iteration budget exhausted ({state.Iteration}/{_maxIterations}) — returning the best-effort answer");
        }

        return state with
        {
            Groundedness = result,
            NextNode = next,
            Exhausted = exhausted,
            Steps = state.Steps.Add(new RagTraceStep
            {
                Name = TraceStepPrefix + CheckGroundednessNode,
                Detail = detail,
                Duration = watch.Elapsed,
                Data = iterationData
                    .Add("grounded", result.IsGrounded ? "true" : "false")
                    .Add("score", result.Score.ToString("F3", CultureInfo.InvariantCulture))
                    .Add("checker", _groundednessChecker.GetType().Name)
                    .Add("next", next),
            }),
        };
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Context assembly aligned with the staged pipeline: token budget (the
    /// first chunk is always kept), anti-Lost-in-the-Middle <c>edges</c> layout,
    /// rank-based <c>[n]</c> markers (<c>[1]</c> = best chunk).
    /// </summary>
    private (string ContextBlock, ImmutableList<ScoredChunk> Kept) AssembleContext(
        ImmutableList<ScoredChunk> ranked)
    {
        var charBudget = (long)_options.Context.MaxTokens * RagDefaults.CharsPerToken;
        var kept = new List<ScoredChunk>(ranked.Count);
        long used = 0;
        foreach (var scored in ranked)
        {
            used += scored.Chunk.Content.Length;
            if (kept.Count > 0 && used > charBudget)
                break;
            kept.Add(scored);
        }

        var order = StagedRagPipeline.ComputeContextOrder(kept.Count, _edgesOrdering);
        var builder = new StringBuilder();
        for (var position = 0; position < order.Length; position++)
        {
            var rank = order[position];
            var chunk = kept[rank].Chunk;
            builder.Append('[').Append(rank + 1).Append("] (source: ").Append(chunk.SourceId).AppendLine(")");
            builder.AppendLine(chunk.Content);
            if (position < order.Length - 1)
                builder.AppendLine();
        }

        return (builder.ToString(), [.. kept]);
    }

    private static RagAnswer Compose(RagGraphState state)
    {
        var text = state.Answer ?? StagedRagPipeline.NoContextAnswer;
        var noContext = string.Equals(text, StagedRagPipeline.NoContextAnswer, StringComparison.Ordinal);

        return new RagAnswer
        {
            Text = text,
            Citations = noContext ? ImmutableList<Citation>.Empty : BuildCitations(state.Chunks),
            Groundedness = state.Groundedness,
            Trace = new RagTrace
            {
                Steps = state.Steps,
                QueryVariants = state.RewrittenQueries,
                Verdicts = state.Verdicts,
                Iterations = state.Iteration,
            },
        };
    }

    private static ImmutableList<Citation> BuildCitations(ImmutableList<ScoredChunk> kept)
    {
        var citations = ImmutableList.CreateBuilder<Citation>();
        for (var rank = 0; rank < kept.Count; rank++)
        {
            var scored = kept[rank];
            var chunk = scored.Chunk;
            citations.Add(new Citation
            {
                Marker = rank + 1,
                ChunkId = chunk.Id,
                SourceId = chunk.SourceId,
                DocumentId = chunk.DocumentId,
                Snippet = chunk.Content.Length <= CitationSnippetLength
                    ? chunk.Content
                    : chunk.Content[..CitationSnippetLength],
                Score = scored.Score,
                StartOffset = chunk.StartOffset,
                EndOffset = chunk.EndOffset,
            });
        }

        return citations.ToImmutable();
    }

    /// <summary>
    /// Normalizes the rewrite output into a single-line probe: first non-empty
    /// line, surrounding quotes stripped; <c>null</c> when nothing usable remains.
    /// </summary>
    internal static string? CleanRewrittenQuery(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return null;

        foreach (var line in responseText.Split('\n'))
        {
            var cleaned = line.Trim().Trim('"', '\'', '`').Trim();
            if (cleaned.Length > 0)
                return cleaned;
        }

        return null;
    }

    private static bool IsEdgesOrdering(string ordering)
    {
        var trimmed = ordering?.Trim() ?? string.Empty;
        if (string.Equals(trimmed, RagDefaults.ContextOrderingEdges, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(trimmed, RagDefaults.ContextOrderingLinear, StringComparison.OrdinalIgnoreCase))
            return false;

        throw new InvalidOperationException(
            $"Unknown RagOptions.Context.Ordering '{ordering}'. Known orderings: " +
            $"{RagDefaults.ContextOrderingEdges}, {RagDefaults.ContextOrderingLinear}.");
    }

    /// <summary>Truncates a traced text (probes and rationales must not bloat the trace).</summary>
    private static string Truncate(string text) =>
        text.Length <= TraceTextLength ? text : text[..TraceTextLength] + "…";

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Corrective graph circuit breaker tripped — degrading to a best-effort answer. {Reason}")]
    private partial void LogCircuitBroken(string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Query rewrite produced no usable text — retrying with the previous probe.")]
    private partial void LogUnusableRewrite();

    [LoggerMessage(Level = LogLevel.Information,
        Message = "No context retrieved from collection '{Collection}' — generation skipped, deterministic no-context answer returned.")]
    private partial void LogNoContextRetrieved(string collection);
}
