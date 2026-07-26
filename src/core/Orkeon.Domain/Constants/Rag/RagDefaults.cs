namespace Orkeon.Domain.Constants.Rag;

/// <summary>
/// Centralised defaults of the RAG subsystem (plan RAG §8.1). Single source for
/// the profile presets (<c>RagProfilePresets</c> in <c>Orkeon.Rag.Abstractions</c>)
/// and the configuration binding of <c>RagOptions</c> on <c>Orkeon:Rag</c>.
/// Replaces the legacy <c>RagDefaults</c> removed in RAG-02 (which carried the
/// toxic <c>MinRelevanceScore = 0.7</c> — deliberately NOT reintroduced: the
/// default is no score floor at all).
/// </summary>
public static class RagDefaults
{
    // ── Profile ───────────────────────────────────────────────────────────

    /// <summary>
    /// Default profile name (<c>fast</c>). The plan (§5.2) targets <c>balanced</c>
    /// as the long-term default, but <c>balanced</c> requires the opt-in ONNX
    /// cross-encoder package (<c>Orkeon.Rag.Onnx</c> + <c>AddOrkeonOnnxReranker()</c>);
    /// defaulting to it would make every bare <c>AddOrkeonRag()</c> host fail loudly
    /// at first query. <c>fast</c> keeps the out-of-the-box behaviour dependency-free;
    /// opt into <c>balanced</c> with one line of configuration (<c>Orkeon:Rag:Profile</c>).
    /// </summary>
    public const string DefaultProfile = "fast";

    // ── Retrieval ─────────────────────────────────────────────────────────

    /// <summary>Default number of chunks kept for context assembly (narrow stage of the 50 → 5 cascade, guide §7.3).</summary>
    public const int TopK = 5;

    /// <summary>Default number of candidates retrieved before rerank (wide stage of the 50 → 5 cascade).</summary>
    public const int CandidateK = 50;

    /// <summary>Wider candidate pool of the <c>quality</c> profile.</summary>
    public const int QualityCandidateK = 100;

    /// <summary>Standard Reciprocal Rank Fusion constant (Cormack et al. 2009).</summary>
    public const int RrfK = 60;

    // ── Query transform ───────────────────────────────────────────────────

    /// <summary>Default query-transform mode: no transformation (RAG-05 adds multi-query / rag-fusion / hyde).</summary>
    public const string QueryTransformNone = "none";

    /// <summary>Default number of variants produced by non-<c>none</c> transformers.</summary>
    public const int QueryTransformVariantCount = 3;

    // ── Rerank ────────────────────────────────────────────────────────────

    /// <summary>Reranker name of the disabled stage (maps to the no-op reranker).</summary>
    public const string RerankNone = "none";

    /// <summary>Reranker name of the ONNX cross-encoder (opt-in <c>Orkeon.Rag.Onnx</c> package).</summary>
    public const string RerankOnnx = "onnx";

    /// <summary>Default number of chunks kept after reranking.</summary>
    public const int RerankTopN = 5;

    // ── Context assembly ──────────────────────────────────────────────────

    /// <summary>Default context budget in tokens (heuristic: 1 token ≈ 4 characters).</summary>
    public const int ContextMaxTokens = 2000;

    /// <summary>Approximate characters per token used by the context budget heuristic.</summary>
    public const int CharsPerToken = 4;

    /// <summary>
    /// Default context ordering: anti-Lost-in-the-Middle <c>edges</c> placement —
    /// the best-ranked chunks land at the extremities of the context block
    /// (odd ranks 1, 3, 5… from the head; even ranks …6, 4, 2 closing the tail).
    /// </summary>
    public const string ContextOrderingEdges = "edges";

    /// <summary>Plain rank-order context layout (best chunk first).</summary>
    public const string ContextOrderingLinear = "linear";
}
