namespace Orkeon.Rag.Abstractions.Options;

/// <summary>
/// Built-in RAG pipeline presets (plan §5.2). Each profile expands to a complete
/// <see cref="RagOptions"/> through <see cref="RagProfilePresets"/>; every value
/// remains individually overridable through configuration.
/// <c>Corrective</c> and <c>Adaptive</c> (graph engine) land with RAG-06.
/// </summary>
public enum RagProfile
{
    /// <summary>Vector-only retrieval, no rerank, direct TopN — lowest latency, zero extra dependency.</summary>
    Fast,

    /// <summary>Hybrid BM25 + RRF retrieval, ONNX cross-encoder rerank (CandidateK 50 → TopN 5).</summary>
    Balanced,

    /// <summary><see cref="Balanced"/> with a wider candidate pool and groundedness verification enabled (checker ships with RAG-06).</summary>
    Quality,
}
