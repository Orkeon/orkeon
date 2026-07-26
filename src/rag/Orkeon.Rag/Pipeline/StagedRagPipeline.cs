using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Constants.Rag;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Retrieval;

namespace Orkeon.Rag.Pipeline;

/// <summary>
/// Staged linear <see cref="IRagPipeline"/> (RAG-04/C4, plan §5.1) — a fixed
/// sequence of togglable stages driven by <see cref="RagOptions"/>:
/// <c>transform</c> (hook, <c>none</c> today — RAG-05) → <c>retrieve</c>
/// (CandidateK candidates, hybrid when the store supports it) → <c>fuse</c>
/// (RRF across variants + dedup) → <c>rerank</c> (named reranker, cascade
/// CandidateK → TopN) → <c>assemble</c> (token budget, anti-Lost-in-the-Middle
/// <c>edges</c> ordering) → <c>generate</c> (grounded, <c>[n]</c> citations) →
/// <c>groundedness</c> (optional hook — checker ships with RAG-06).
/// Every stage is traced in <see cref="RagAnswer"/>.<see cref="RagAnswer.Trace"/>.
/// </summary>
/// <remarks>
/// <para><b>Anti-Lost-in-the-Middle ordering</b> (<c>Context.Ordering: edges</c>,
/// the default): ranked chunks r1 (best) … rm are laid out in the context block
/// as <c>r1, r3, r5, …</c> from the head, then <c>…, r6, r4, r2</c> closing the
/// tail — the two strongest chunks sit at the extremities, the weakest in the
/// middle. Citation markers stay <b>rank-based</b> (<c>[1]</c> = best chunk)
/// whatever the layout, so markers are stable across orderings.</para>
/// <para>When retrieval yields no candidate, generation is skipped and
/// <see cref="NoContextAnswer"/> is returned with an explanatory trace — the
/// pipeline never lets the model answer ungrounded.</para>
/// </remarks>
public sealed partial class StagedRagPipeline : IRagPipeline
{
    /// <summary>Default grounded system prompt (anti-hallucination, <c>[n]</c> citation markers).</summary>
    public const string DefaultSystemPrompt =
        "You are a retrieval-augmented assistant. Answer ONLY from the provided context. " +
        "Cite the context passages you use with their [n] markers. " +
        "If the context does not contain the answer, say so explicitly instead of guessing.";

    /// <summary>Deterministic answer returned when retrieval yields no candidate.</summary>
    public const string NoContextAnswer =
        "No relevant context was found in the knowledge base for this question.";

    private const int CitationSnippetLength = 200;

    private readonly IDocumentStore _store;
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IChatClient _chatClient;
    private readonly RagOptions _options;
    private readonly QueryTransformerFactory? _queryTransformers;
    private readonly RerankerFactory? _rerankers;
    private readonly IGroundednessChecker? _groundednessChecker;
    private readonly ILogger<StagedRagPipeline> _logger;
    private readonly bool _edgesOrdering;

    /// <summary>Initializes the staged pipeline.</summary>
    /// <param name="store">Document store queried at retrieval.</param>
    /// <param name="embeddingProvider">Application embedding port used to embed the query (and its variants).</param>
    /// <param name="chatClient">Chat client used for grounded generation.</param>
    /// <param name="options">Pipeline options; <c>null</c> selects the defaults (<c>fast</c>-equivalent stages).</param>
    /// <param name="queryTransformers">
    /// Named query-transformer factory. Required only when
    /// <see cref="RagQueryTransformOptions.Mode"/> is not <c>none</c>.
    /// </param>
    /// <param name="rerankers">
    /// Named reranker factory. Required only when
    /// <see cref="RagRerankOptions.Enabled"/> is set.
    /// </param>
    /// <param name="groundednessChecker">
    /// Optional groundedness checker (RAG-06). Absent while
    /// <see cref="RagGroundednessOptions.Enabled"/> is set, the stage is traced
    /// as skipped.
    /// </param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    /// <exception cref="InvalidOperationException">
    /// The options request a stage whose collaborator is missing, or carry an
    /// unknown <see cref="RagContextOptions.Ordering"/>.
    /// </exception>
    public StagedRagPipeline(
        IDocumentStore store,
        IEmbeddingProvider embeddingProvider,
        IChatClient chatClient,
        RagOptions? options = null,
        QueryTransformerFactory? queryTransformers = null,
        RerankerFactory? rerankers = null,
        IGroundednessChecker? groundednessChecker = null,
        ILogger<StagedRagPipeline>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(embeddingProvider);
        ArgumentNullException.ThrowIfNull(chatClient);

        _store = store;
        _embeddingProvider = embeddingProvider;
        _chatClient = chatClient;
        _options = options ?? new RagOptions();
        _queryTransformers = queryTransformers;
        _rerankers = rerankers;
        _groundednessChecker = groundednessChecker;
        _logger = logger ?? NullLogger<StagedRagPipeline>.Instance;

        ValidateOptions(_options);
        _edgesOrdering = IsEdgesOrdering(_options.Context.Ordering);

        if (!IsNoneTransform(_options.QueryTransform.Mode) && _queryTransformers is null)
        {
            throw new InvalidOperationException(
                $"RagOptions.QueryTransform.Mode is '{_options.QueryTransform.Mode}' but no query-transformer " +
                "factory was provided. Pass the QueryTransformerFactory (AddOrkeonRag registers one).");
        }

        if (_options.Rerank.Enabled && _rerankers is null)
        {
            throw new InvalidOperationException(
                "RagOptions.Rerank.Enabled is set but no reranker factory was provided. " +
                "Pass the RerankerFactory (AddOrkeonRag registers one).");
        }
    }

    /// <summary>The effective options of this pipeline instance.</summary>
    public RagOptions Options => _options;

    /// <inheritdoc />
    public async Task<RagAnswer> QueryAsync(
        RagQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Collection);

        var steps = ImmutableList.CreateBuilder<RagTraceStep>();
        var topN = Math.Max(1, query.TopN);

        // 1 — transform: 0..n retrieval-friendly variants (none = passthrough hook, RAG-05).
        var variants = await TransformAsync(query.Text, steps, cancellationToken).ConfigureAwait(false);

        // 2 — retrieve: CandidateK candidates per variant (hybrid honoured by capable stores).
        var rankings = await RetrieveAsync(query, variants, topN, steps, cancellationToken).ConfigureAwait(false);

        // 3 — fuse: RRF across variant rankings + dedup by chunk id.
        var candidates = Fuse(rankings, topN, steps);

        // 4 — rerank: named reranker, cascade CandidateK → TopN.
        var ranked = await RerankAsync(query.Text, candidates, topN, steps, cancellationToken).ConfigureAwait(false);

        if (ranked.Count == 0)
        {
            LogNoContextRetrieved(query.Collection);
            steps.Add(new RagTraceStep
            {
                Name = "generate",
                Detail = "skipped — no retrieved context",
            });

            return new RagAnswer
            {
                Text = NoContextAnswer,
                Citations = ImmutableList<Citation>.Empty,
                Trace = BuildTrace(steps, variants),
            };
        }

        // 5 — assemble: token budget + anti-Lost-in-the-Middle edges ordering.
        var (contextBlock, kept) = Assemble(ranked, steps);

        // 6 — generate: grounded answer with [n] citation markers.
        var text = await GenerateAsync(query.Text, contextBlock, steps, cancellationToken).ConfigureAwait(false);

        // 7 — groundedness: optional verification hook (checker ships with RAG-06).
        var groundedness = await CheckGroundednessAsync(query.Text, text, kept, steps, cancellationToken)
            .ConfigureAwait(false);

        return new RagAnswer
        {
            Text = text,
            Citations = BuildCitations(kept),
            Trace = BuildTrace(steps, variants),
            Groundedness = groundedness,
        };
    }

    // ── stages ─────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<string>> TransformAsync(
        string queryText,
        ImmutableList<RagTraceStep>.Builder steps,
        CancellationToken cancellationToken)
    {
        var mode = _options.QueryTransform.Mode;
        if (IsNoneTransform(mode))
        {
            steps.Add(new RagTraceStep
            {
                Name = "transform",
                Detail = "none — passthrough",
                Data = ImmutableDictionary<string, string>.Empty.Add("mode", RagDefaults.QueryTransformNone),
            });
            return [];
        }

        var watch = Stopwatch.StartNew();
        var transformer = _queryTransformers!.Create(mode); // unknown name fails loudly
        var variants = await transformer.TransformAsync(
                queryText,
                new QueryTransformContext { MaxVariants = _options.QueryTransform.VariantCount },
                cancellationToken)
            .ConfigureAwait(false);
        watch.Stop();

        steps.Add(new RagTraceStep
        {
            Name = "transform",
            Duration = watch.Elapsed,
            Data = ImmutableDictionary<string, string>.Empty
                .Add("mode", transformer.Name)
                .Add("variants", variants.Count.ToString(CultureInfo.InvariantCulture)),
        });

        return variants;
    }

    private async Task<List<IReadOnlyList<ScoredChunk>>> RetrieveAsync(
        RagQuery query,
        IReadOnlyList<string> variants,
        int topN,
        ImmutableList<RagTraceStep>.Builder steps,
        CancellationToken cancellationToken)
    {
        var width = Math.Max(_options.Retrieval.CandidateK, topN);
        var hybrid = _options.Retrieval.Hybrid.Enabled;
        var texts = new List<string>(1 + variants.Count) { query.Text };
        texts.AddRange(variants.Where(v => !string.IsNullOrWhiteSpace(v)
            && !string.Equals(v, query.Text, StringComparison.Ordinal)));

        var watch = Stopwatch.StartNew();
        var rankings = new List<IReadOnlyList<ScoredChunk>>(texts.Count);
        var vectorMode = false;
        var total = 0;

        foreach (var text in texts)
        {
            var embedding = await _embeddingProvider.GetEmbeddingAsync(text, cancellationToken)
                .ConfigureAwait(false);
            vectorMode |= embedding.Length > 0;

            var hits = await _store.SearchAsync(
                    query.Collection,
                    new RetrievalQuery
                    {
                        Text = text,
                        Embedding = embedding.Length > 0 ? [.. embedding] : null,
                        TopK = width,
                        Filters = query.Filters,
                        Hybrid = hybrid,
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (_options.Retrieval.MinScore is { } floor)
                hits = [.. hits.Where(hit => hit.Score >= floor)];

            total += hits.Count;
            rankings.Add(hits);
        }

        watch.Stop();
        steps.Add(new RagTraceStep
        {
            Name = "retrieve",
            Duration = watch.Elapsed,
            Data = ImmutableDictionary<string, string>.Empty
                .Add("candidates", total.ToString(CultureInfo.InvariantCulture))
                .Add("top_k", width.ToString(CultureInfo.InvariantCulture))
                .Add("mode", vectorMode ? "vector" : "text")
                .Add("hybrid", hybrid ? "true" : "false")
                .Add("variants", (texts.Count - 1).ToString(CultureInfo.InvariantCulture)),
        });

        return rankings;
    }

    private IReadOnlyList<ScoredChunk> Fuse(
        List<IReadOnlyList<ScoredChunk>> rankings,
        int topN,
        ImmutableList<RagTraceStep>.Builder steps)
    {
        var width = Math.Max(_options.Retrieval.CandidateK, topN);
        var input = rankings.Sum(r => r.Count);
        var watch = Stopwatch.StartNew();

        IReadOnlyList<ScoredChunk> fused;
        string method;
        if (rankings.Count > 1)
        {
            fused = ReciprocalRankFusion.Fuse(_options.Retrieval.Hybrid.RrfK, width, [.. rankings]);
            method = "rrf";
        }
        else
        {
            fused = DedupByChunkId(rankings.Count == 1 ? rankings[0] : []);
            method = "dedup";
        }

        watch.Stop();
        steps.Add(new RagTraceStep
        {
            Name = "fuse",
            Duration = watch.Elapsed,
            Data = ImmutableDictionary<string, string>.Empty
                .Add("in", input.ToString(CultureInfo.InvariantCulture))
                .Add("out", fused.Count.ToString(CultureInfo.InvariantCulture))
                .Add("method", method),
        });

        return fused;
    }

    private async Task<IReadOnlyList<ScoredChunk>> RerankAsync(
        string queryText,
        IReadOnlyList<ScoredChunk> candidates,
        int topN,
        ImmutableList<RagTraceStep>.Builder steps,
        CancellationToken cancellationToken)
    {
        if (!_options.Rerank.Enabled)
        {
            var truncated = candidates.Take(topN).ToList();
            steps.Add(new RagTraceStep
            {
                Name = "rerank",
                Detail = "disabled — truncation to TopN",
                Data = ImmutableDictionary<string, string>.Empty
                    .Add("reranker", RagDefaults.RerankNone)
                    .Add("candidates", candidates.Count.ToString(CultureInfo.InvariantCulture))
                    .Add("kept", truncated.Count.ToString(CultureInfo.InvariantCulture)),
            });
            return truncated;
        }

        var watch = Stopwatch.StartNew();
        var reranker = _rerankers!.Create(_options.Rerank.Kind); // unknown name fails loudly
        var ranked = await reranker.RerankAsync(queryText, candidates, topN, cancellationToken)
            .ConfigureAwait(false);
        watch.Stop();

        steps.Add(new RagTraceStep
        {
            Name = "rerank",
            Duration = watch.Elapsed,
            Data = ImmutableDictionary<string, string>.Empty
                .Add("reranker", reranker.Name)
                .Add("candidates", candidates.Count.ToString(CultureInfo.InvariantCulture))
                .Add("kept", ranked.Count.ToString(CultureInfo.InvariantCulture)),
        });

        return ranked;
    }

    private (string ContextBlock, List<ScoredChunk> Kept) Assemble(
        IReadOnlyList<ScoredChunk> ranked,
        ImmutableList<RagTraceStep>.Builder steps)
    {
        var watch = Stopwatch.StartNew();

        // Budget: chunks are selected by rank; the block never exceeds the
        // (approximate) token budget — the first chunk is always kept.
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

        var order = ComputeContextOrder(kept.Count, _edgesOrdering);
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

        watch.Stop();
        steps.Add(new RagTraceStep
        {
            Name = "assemble",
            Duration = watch.Elapsed,
            Data = ImmutableDictionary<string, string>.Empty
                .Add("chunks", kept.Count.ToString(CultureInfo.InvariantCulture))
                .Add("ordering", _edgesOrdering ? RagDefaults.ContextOrderingEdges : RagDefaults.ContextOrderingLinear)
                .Add("dropped_by_budget", (ranked.Count - kept.Count).ToString(CultureInfo.InvariantCulture)),
        });

        return (builder.ToString(), kept);
    }

    private async Task<string> GenerateAsync(
        string queryText,
        string contextBlock,
        ImmutableList<RagTraceStep>.Builder steps,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, _options.Generation.SystemPrompt ?? DefaultSystemPrompt),
            new(ChatRole.User, $"Context:\n{contextBlock}\n\nQuestion: {queryText}"),
        };

        var chatOptions = new ChatOptions
        {
            Temperature = _options.Generation.Temperature,
            MaxOutputTokens = _options.Generation.MaxOutputTokens,
        };

        var response = await _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken)
            .ConfigureAwait(false);
        watch.Stop();

        var data = ImmutableDictionary<string, string>.Empty;
        if (!string.IsNullOrEmpty(response.ModelId))
            data = data.Add("model", response.ModelId);

        steps.Add(new RagTraceStep
        {
            Name = "generate",
            Duration = watch.Elapsed,
            Data = data,
        });

        return response.Text ?? string.Empty;
    }

    private async Task<GroundednessResult?> CheckGroundednessAsync(
        string question,
        string answer,
        IReadOnlyList<ScoredChunk> context,
        ImmutableList<RagTraceStep>.Builder steps,
        CancellationToken cancellationToken)
    {
        if (!_options.Groundedness.Enabled)
            return null;

        if (_groundednessChecker is null)
        {
            steps.Add(new RagTraceStep
            {
                Name = "groundedness",
                Detail = "skipped — no IGroundednessChecker registered (ships with RAG-06)",
            });
            return null;
        }

        var watch = Stopwatch.StartNew();
        var result = await _groundednessChecker.CheckAsync(question, answer, context, cancellationToken)
            .ConfigureAwait(false);
        watch.Stop();

        steps.Add(new RagTraceStep
        {
            Name = "groundedness",
            Duration = watch.Elapsed,
            Data = ImmutableDictionary<string, string>.Empty
                .Add("grounded", result.IsGrounded ? "true" : "false")
                .Add("score", result.Score.ToString("F3", CultureInfo.InvariantCulture)),
        });

        return result;
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Positions of the ranked chunks inside the context block. <c>edges</c>
    /// (anti-Lost-in-the-Middle): 0-based ranks <c>0, 2, 4, …</c> open the block,
    /// <c>…, 5, 3, 1</c> close it — i.e. 1-based ranks <c>1, 3, 5, …, 6, 4, 2</c>,
    /// best chunk first, second-best last, weakest in the middle.
    /// <c>linear</c>: plain rank order.
    /// </summary>
    internal static int[] ComputeContextOrder(int count, bool edges)
    {
        var order = new int[count];
        if (!edges)
        {
            for (var i = 0; i < count; i++)
                order[i] = i;
            return order;
        }

        var position = 0;
        for (var rank = 0; rank < count; rank += 2)
            order[position++] = rank;

        var lastOdd = count % 2 == 0 ? count - 1 : count - 2;
        for (var rank = lastOdd; rank >= 1; rank -= 2)
            order[position++] = rank;

        return order;
    }

    private static IReadOnlyList<ScoredChunk> DedupByChunkId(IReadOnlyList<ScoredChunk> ranking)
    {
        if (ranking.Count <= 1)
            return ranking;

        var seen = new HashSet<string>(ranking.Count, StringComparer.Ordinal);
        var result = new List<ScoredChunk>(ranking.Count);
        foreach (var scored in ranking)
        {
            if (seen.Add(scored.Chunk.Id))
                result.Add(scored);
        }

        return result;
    }

    private static ImmutableList<Citation> BuildCitations(List<ScoredChunk> kept)
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

    private static RagTrace BuildTrace(
        ImmutableList<RagTraceStep>.Builder steps,
        IReadOnlyList<string> variants)
        => new()
        {
            Steps = steps.ToImmutable(),
            QueryVariants = [.. variants],
        };

    private static bool IsNoneTransform(string mode)
        => string.IsNullOrWhiteSpace(mode)
            || string.Equals(mode.Trim(), RagDefaults.QueryTransformNone, StringComparison.OrdinalIgnoreCase);

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

    private static void ValidateOptions(RagOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Retrieval.TopK, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Retrieval.CandidateK, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Retrieval.Hybrid.RrfK, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Rerank.TopN, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Context.MaxTokens, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.QueryTransform.VariantCount, nameof(options));
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "No context retrieved from collection '{Collection}' — generation skipped, deterministic no-context answer returned.")]
    private partial void LogNoContextRetrieved(string collection);
}
