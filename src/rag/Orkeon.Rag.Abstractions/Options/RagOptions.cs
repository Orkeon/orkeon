using Orkeon.Domain.Constants.Rag;

namespace Orkeon.Rag.Abstractions.Options;

/// <summary>
/// Complete configuration tree of the RAG query pipeline (v2, plan §8.1), bound
/// from the <c>Orkeon:Rag</c> configuration section. A <see cref="Profile"/>
/// selects a preset (<see cref="RagProfilePresets"/>); every value can then be
/// overridden individually through configuration — profile = preset,
/// configuration = override.
/// </summary>
/// <remarks>
/// Properties are mutable on purpose: instances start from a preset and are
/// completed by <c>IConfiguration.Bind</c>. Defaults are sourced from
/// <see cref="RagDefaults"/> (Domain constants).
/// </remarks>
public sealed class RagOptions
{
    /// <summary>
    /// Profile name (<c>fast</c>, <c>balanced</c>, <c>quality</c>). Defaults to
    /// <see cref="RagDefaults.DefaultProfile"/> (<c>fast</c> — <c>balanced</c>
    /// needs the opt-in ONNX reranker package). Unknown names fail loudly.
    /// </summary>
    public string Profile { get; set; } = RagDefaults.DefaultProfile;

    /// <summary>Default collection queried when the call site names none.</summary>
    public string? Collection { get; set; }

    /// <summary>Retrieval stage options.</summary>
    public RagRetrievalOptions Retrieval { get; set; } = new();

    /// <summary>Query-transform stage options (hook — RAG-05 ships the transformers).</summary>
    public RagQueryTransformOptions QueryTransform { get; set; } = new();

    /// <summary>Rerank stage options.</summary>
    public RagRerankOptions Rerank { get; set; } = new();

    /// <summary>Context-assembly stage options (budget, anti-Lost-in-the-Middle ordering).</summary>
    public RagContextOptions Context { get; set; } = new();

    /// <summary>Groundedness verification options (hook — RAG-06 ships the checker).</summary>
    public RagGroundednessOptions Groundedness { get; set; } = new();

    /// <summary>Grounded-generation options (system prompt, sampling).</summary>
    public RagGenerationOptions Generation { get; set; } = new();
}

/// <summary>Retrieval stage of <see cref="RagOptions"/>.</summary>
public sealed class RagRetrievalOptions
{
    /// <summary>Default number of chunks kept for context assembly (a call-site <c>RagQuery.TopN</c> wins).</summary>
    public int TopK { get; set; } = RagDefaults.TopK;

    /// <summary>
    /// Number of candidates retrieved before fusion/rerank (wide stage of the
    /// 50 → 5 cascade). The pipeline always retrieves at least the final TopN.
    /// </summary>
    public int CandidateK { get; set; } = RagDefaults.CandidateK;

    /// <summary>
    /// Optional score floor applied to raw retrieval scores before fusion.
    /// <c>null</c> (default) applies none — the toxic global 0.7 floor of the
    /// legacy subsystem is deliberately gone.
    /// </summary>
    public double? MinScore { get; set; }

    /// <summary>Hybrid (BM25 + vector) retrieval options.</summary>
    public RagHybridOptions Hybrid { get; set; } = new();
}

/// <summary>Hybrid retrieval node of <see cref="RagRetrievalOptions"/>.</summary>
public sealed class RagHybridOptions
{
    /// <summary>
    /// Whether retrieval fuses lexical (BM25 / native full-text) and vector
    /// rankings. Honoured per query through the hybrid-capable document store.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Reciprocal Rank Fusion constant (must be positive; 60 is the standard).</summary>
    public int RrfK { get; set; } = RagDefaults.RrfK;
}

/// <summary>Query-transform stage of <see cref="RagOptions"/> (RAG-05 hook).</summary>
public sealed class RagQueryTransformOptions
{
    /// <summary>
    /// Transformer name resolved through the query-transformer factory
    /// (<c>none</c> today; <c>multi-query</c> / <c>rag-fusion</c> / <c>hyde</c>
    /// land with RAG-05). <c>none</c> skips the stage; unknown names fail loudly.
    /// </summary>
    public string Mode { get; set; } = RagDefaults.QueryTransformNone;

    /// <summary>Number of variants requested from non-<c>none</c> transformers.</summary>
    public int VariantCount { get; set; } = RagDefaults.QueryTransformVariantCount;
}

/// <summary>Rerank stage of <see cref="RagOptions"/>.</summary>
public sealed class RagRerankOptions
{
    /// <summary>Whether the rerank stage runs. Disabled, candidates are truncated to TopN in retrieval order.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Reranker name resolved through the reranker factory
    /// (<c>none</c>/<c>noop</c>, <c>llm</c>/<c>listwise</c>,
    /// <c>onnx</c>/<c>cross-encoder</c> — the latter via the opt-in
    /// <c>Orkeon.Rag.Onnx</c> package). Unknown names fail loudly.
    /// </summary>
    public string Kind { get; set; } = RagDefaults.RerankNone;

    /// <summary>Default number of chunks kept after reranking (a call-site <c>RagQuery.TopN</c> wins).</summary>
    public int TopN { get; set; } = RagDefaults.RerankTopN;
}

/// <summary>Context-assembly stage of <see cref="RagOptions"/>.</summary>
public sealed class RagContextOptions
{
    /// <summary>Context budget in tokens (1 token ≈ 4 characters heuristic; excess chunks are dropped, never silently generated over).</summary>
    public int MaxTokens { get; set; } = RagDefaults.ContextMaxTokens;

    /// <summary>
    /// Chunk layout inside the context block: <c>edges</c> (default —
    /// anti-Lost-in-the-Middle: odd ranks 1, 3, 5… open the block, even ranks
    /// …6, 4, 2 close it, so the two best chunks sit at the extremities) or
    /// <c>linear</c> (plain rank order). Citation markers stay rank-based
    /// (<c>[1]</c> = best chunk) whatever the layout.
    /// </summary>
    public string Ordering { get; set; } = RagDefaults.ContextOrderingEdges;
}

/// <summary>Groundedness verification node of <see cref="RagOptions"/> (RAG-06 hook).</summary>
public sealed class RagGroundednessOptions
{
    /// <summary>
    /// Whether the answer is verified against the retrieved context after
    /// generation. Requires an <c>IGroundednessChecker</c> registration (RAG-06);
    /// enabled without one, the stage is traced as skipped.
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>Grounded-generation node of <see cref="RagOptions"/>.</summary>
public sealed class RagGenerationOptions
{
    /// <summary>Grounded system prompt; <c>null</c> selects the pipeline default (anti-hallucination, <c>[n]</c> markers).</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>Sampling temperature passed to the chat client.</summary>
    public float? Temperature { get; set; }

    /// <summary>Maximum output tokens passed to the chat client.</summary>
    public int? MaxOutputTokens { get; set; }
}
