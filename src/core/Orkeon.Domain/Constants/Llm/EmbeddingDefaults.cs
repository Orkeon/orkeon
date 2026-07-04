namespace Orkeon.Domain.Constants.Llm;

/// <summary>
/// Centralised embedding constants used across the Orkeon platform.
/// Eliminates scattered magic integer literals and provides a single
/// place to tune default embedding dimensions, providers, models and batch sizes.
/// </summary>
public static class EmbeddingDefaults
{
    // ── Dimension ───────────────────────────────────────────────────────

    /// <summary>
    /// Default embedding vector dimension (1 536).
    /// Matches the OpenAI text-embedding-ada-002 / text-embedding-3-small standard,
    /// which is the default provider configured in <c>EmbeddingOptions</c> (Application layer).
    /// </summary>
    public const int DefaultDimension = 1_536;

    // ── Local / Simple providers ─────────────────────────────────────────

    /// <summary>
    /// Embedding dimension for local/simple providers such as sentence-transformers (384).
    /// Use this constant for development stubs (<c>SimpleEmbeddingService</c>,
    /// <c>HashBasedEmbeddingProvider</c>) and opt-in local deployments.
    /// </summary>
    public const int LocalDimension = 384;

    // ── Provider ──────────────────────────────────────────────────────────

    /// <summary>
    /// Default embedding provider name ("openai").
    /// Used in <c>EmbeddingOptions</c>.
    /// </summary>
    public const string DefaultProvider = "openai";

    /// <summary>
    /// Fallback embedding provider name used when no external provider is configured ("Simple").
    /// Used in <c>OrkeonApplicationOptions</c> development/stub mode.
    /// </summary>
    public const string FallbackProvider = "Simple";

    // ── Models ────────────────────────────────────────────────────────────

    /// <summary>
    /// Default OpenAI embedding model name ("text-embedding-3-small").
    /// Used in <c>EmbeddingOptions</c>.
    /// </summary>
    public const string DefaultModel = "text-embedding-3-small";

    /// <summary>
    /// Legacy OpenAI embedding model name ("text-embedding-ada-002").
    /// Retained for backward-compatibility scenarios (e.g. Azure OpenAI deployments).
    /// </summary>
    public const string DefaultOpenAIModel = "text-embedding-ada-002";

    // ── Batch ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Default maximum batch size for bulk embedding requests (100).
    /// </summary>
    public const int DefaultBatchSize = 100;
}
