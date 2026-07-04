namespace Orkeon.Analysis.Core.Cache;

internal sealed record CacheManifest
{
    public string IndexId { get; init; } = string.Empty;
    public Dictionary<string, string> Files { get; init; } = [];
    public Dictionary<string, string> AdapterVersions { get; init; } = [];

    /// <summary>
    /// Identity of the embedding provider that produced this cache.
    /// When non-null, <see cref="RaggableTreeCache.IsValidAsync"/> requires the
    /// active provider to match (Provider, Model, Dimensions) — otherwise the
    /// cache is considered stale (e.g. switching from OpenAI 1536-dim to a local
    /// 384-dim provider invalidates the cache cleanly instead of corrupting it).
    /// </summary>
    public EmbeddingCacheProfile? Embedding { get; init; }
}

/// <summary>
/// Triplet identifying the embedding provider that produced a cached
/// <see cref="Orkeon.Analysis.Abstractions.Models.RaggableTree"/>.
/// Used as part of the cache validity key so that switching providers or
/// changing the embedding dimension forces a clean re-index.
/// </summary>
public sealed record EmbeddingCacheProfile
{
    public string Provider { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public int Dimensions { get; init; }
}
