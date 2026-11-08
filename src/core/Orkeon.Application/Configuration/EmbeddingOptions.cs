using static Orkeon.Domain.Constants.Llm.EmbeddingDefaults;

namespace Orkeon.Application.Configuration;

/// <summary>
/// Configuration options for embedding providers.
/// </summary>
public class EmbeddingOptions
{
    /// <summary>
    /// The embedding provider to use: "openai", "ollama", "mistral", "azure-openai" or "gemini".
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

    /// <summary>
    /// Base URL of the embeddings endpoint. Empty: the provider's public endpoint. Required by
    /// <c>azure-openai</c> (the resource URL, <c>https://&lt;resource&gt;.openai.azure.com</c>).
    /// </summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// The API key, in clear text, for <c>mistral</c>, <c>azure-openai</c> and <c>gemini</c>. Prefer
    /// <see cref="ApiKeyEnvVar"/>: a key written here is in the settings file.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Name of the environment variable holding the API key, read when <see cref="ApiKey"/> is empty.</summary>
    public string ApiKeyEnvVar { get; set; } = "";

    /// <summary>The <c>api-version</c> query of <c>azure-openai</c>. Empty: the provider's default.</summary>
    public string ApiVersion { get; set; } = "";

    /// <summary>
    /// Virtual path of the file recording the provider, model and dimensions the stored vectors came from;
    /// the host refuses to start when they change. Empty: the guard is off.
    /// </summary>
    public string FingerprintPath { get; set; } = "/workspace/.orkeon/embedding-fingerprint.json";
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
