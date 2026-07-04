namespace Orkeon.Domain.Constants.Memory;

/// <summary>
/// Centralised search and retrieval constants used across the Orkeon platform.
/// Separates similarity/vector search concepts from LLM temperature to eliminate
/// ambiguous magic-value usage of 0.7.
/// </summary>
public static class SearchDefaults
{
    // ── Similarity Threshold ─────────────────────────────────────────────

    /// <summary>
    /// Default minimum cosine-similarity score for a memory or knowledge entry to be
    /// considered a relevant match during semantic search (0.7).
    /// Range: 0.0 (no filter) to 1.0 (exact match only).
    /// </summary>
    public const double DefaultSimilarityThreshold = 0.7;

    // ── Vector Weight ────────────────────────────────────────────────────

    /// <summary>
    /// Default weight assigned to the vector (semantic) component in hybrid search
    /// queries that blend keyword and vector scores (0.7).
    /// Range: 0.0 (keyword only) to 1.0 (vector only).
    /// </summary>
    public const double DefaultVectorWeight = 0.7;
}
