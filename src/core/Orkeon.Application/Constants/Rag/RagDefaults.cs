namespace Orkeon.Application.Constants.Rag;

/// <summary>
/// Default values for RAG (Retrieval-Augmented Generation) pipeline configuration.
/// Centralises magic numbers used across retrieval, augmentation, and generation stages.
/// </summary>
public static class RagDefaults
{
    // ── Retrieval ───────────────────────────────────────────────────────────

    /// <summary>Default maximum number of chunks to retrieve from knowledge sources.</summary>
    public const int DefaultTopK = 5;

    /// <summary>
    /// Default minimum relevance score threshold (0.0–1.0). Chunks below this are filtered out.
    /// Kept low on purpose: it only discards clear noise while <see cref="DefaultTopK"/> ranking
    /// does the actual selection — real-world cosine scores for relevant chunks routinely sit
    /// below 0.7 depending on the embedding model, so a high default silently returns nothing.
    /// </summary>
    public const float DefaultMinRelevanceScore = 0.3f;

    /// <summary>Default weight for semantic similarity in hybrid search (0.0–1.0).</summary>
    public const float DefaultSemanticWeight = 0.7f;

    /// <summary>Default weight for keyword matching in hybrid search (0.0–1.0).</summary>
    public const float DefaultKeywordWeight = 0.3f;

    // ── Augmentation ────────────────────────────────────────────────────────

    /// <summary>Default maximum number of chunks to include in the prompt.</summary>
    public const int DefaultMaxChunksInPrompt = 3;

    /// <summary>Default maximum estimated token budget for the context block.</summary>
    public const int DefaultMaxContextTokens = 2000;

    // ── Generation ──────────────────────────────────────────────────────────

    /// <summary>Default temperature for LLM generation. Lower values produce more deterministic output.</summary>
    public const float DefaultTemperature = 0.3f;

    /// <summary>Default maximum tokens in the generated response.</summary>
    public const int DefaultMaxTokens = 1000;
}
