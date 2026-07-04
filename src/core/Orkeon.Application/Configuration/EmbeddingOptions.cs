using static Orkeon.Domain.Constants.Llm.EmbeddingDefaults;

namespace Orkeon.Application.Configuration;

/// <summary>
/// Configuration options for embedding providers.
/// </summary>
public class EmbeddingOptions
{
    /// <summary>
    /// The embedding provider to use (e.g., "openai", "ollama").
    /// </summary>
    public string Provider { get; set; } = DefaultProvider;

    /// <summary>
    /// The model name to use for embeddings.
    /// </summary>
    public string Model { get; set; } = DefaultModel;

    /// <summary>
    /// The dimension of the embedding vectors.
    /// </summary>
    public int Dimension { get; set; } = DefaultDimension;

    /// <summary>
    /// Maximum batch size for bulk embedding requests.
    /// </summary>
    public int BatchSize { get; set; } = DefaultBatchSize;

    /// <summary>
    /// Whether to enable embedding caching.
    /// </summary>
    public bool EnableCache { get; set; } = true;
}

/// <summary>
/// Configuration options for the embedding cache.
/// </summary>
public class EmbeddingCacheOptions
{
    /// <summary>
    /// Sliding expiration time in minutes for cached embeddings.
    /// </summary>
    public int SlidingExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// Maximum total size of the cache in bytes.
    /// </summary>
    public long MaxCacheSizeBytes { get; set; } = 100 * 1024 * 1024; // 100 MB
}
