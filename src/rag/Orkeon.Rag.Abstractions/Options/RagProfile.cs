namespace Orkeon.Rag.Abstractions.Options;

/// <summary>
/// Built-in RAG pipeline presets (plan §5.2). Each profile expands to a complete
/// <see cref="RagOptions"/> through <see cref="RagProfilePresets"/>; every value
/// remains individually overridable through configuration.
/// </summary>
public enum RagProfile
{
    /// <summary>Vector-only retrieval, no rerank, direct TopN — lowest latency, zero extra dependency.</summary>
    Fast,

    /// <summary>Hybrid BM25 + RRF retrieval, ONNX cross-encoder rerank (CandidateK 50 → TopN 5).</summary>
    Balanced,

    /// <summary><see cref="Balanced"/> with a wider candidate pool and groundedness verification enabled (checker shipped with RAG-06).</summary>
    Quality,

    /// <summary>
    /// Adaptive-RAG routing (RAG-05/C3, guide §8.4): a query-complexity classifier
    /// routes each query — <c>NoRetrieval</c> answers directly from the model
    /// (no retrieval, empty citations), <c>SingleShot</c> delegates to
    /// <see cref="Balanced"/>, <c>Iterative</c> delegates to <see cref="Corrective"/>
    /// (since RAG-06 — the former documented fallback to <see cref="Quality"/> is
    /// lifted). The routing pipeline is composed by the profile resolver, not
    /// expanded from a <see cref="RagOptions"/> preset.
    /// </summary>
    Adaptive,

    /// <summary>
    /// Corrective RAG (CRAG, RAG-06, guide §8): the query runs the corrective
    /// graph engine (retrieve → evaluate → rewrite/refine/web-fallback loops on
    /// the Graph orchestration mode) instead of the linear staged pipeline. The
    /// pipeline is composed by the profile resolver from the corrective graph
    /// services (<c>AddOrkeonCorrectiveRag</c>); the preset options feed the
    /// graph's retrieve/generate nodes.
    /// </summary>
    Corrective,
}
