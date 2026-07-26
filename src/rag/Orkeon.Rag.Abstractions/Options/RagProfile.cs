namespace Orkeon.Rag.Abstractions.Options;

/// <summary>
/// Built-in RAG pipeline presets (plan §5.2). Each profile expands to a complete
/// <see cref="RagOptions"/> through <see cref="RagProfilePresets"/>; every value
/// remains individually overridable through configuration.
/// <c>Corrective</c> (graph engine) lands with RAG-06.
/// </summary>
public enum RagProfile
{
    /// <summary>Vector-only retrieval, no rerank, direct TopN — lowest latency, zero extra dependency.</summary>
    Fast,

    /// <summary>Hybrid BM25 + RRF retrieval, ONNX cross-encoder rerank (CandidateK 50 → TopN 5).</summary>
    Balanced,

    /// <summary><see cref="Balanced"/> with a wider candidate pool and groundedness verification enabled (checker ships with RAG-06).</summary>
    Quality,

    /// <summary>
    /// Adaptive-RAG routing (RAG-05/C3, guide §8.4): a query-complexity classifier
    /// routes each query — <c>NoRetrieval</c> answers directly from the model
    /// (no retrieval, empty citations), <c>SingleShot</c> delegates to
    /// <see cref="Balanced"/>, <c>Iterative</c> falls back to <see cref="Quality"/>
    /// until the corrective engine ships (RAG-06). The routing pipeline is composed
    /// by the profile resolver, not expanded from a <see cref="RagOptions"/> preset.
    /// </summary>
    Adaptive,
}
