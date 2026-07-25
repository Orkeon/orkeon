namespace Orkeon.Rag.Ingestion;

/// <summary>
/// Per-collection ingestion manifest (RAG-03/C1, plan §6.3): records, for every
/// ingested source, the content hash, the chunker configuration, and the
/// collection-wide embedding profile. Persisted as one JSON file per collection
/// via <see cref="IIngestionManifestStore"/> and used by the ingestion pipeline
/// to skip unchanged sources (0 embeddings), re-ingest only modified ones, and
/// fail loudly on embedding-model drift.
/// </summary>
public sealed record IngestionManifest
{
    /// <summary>Collection this manifest describes.</summary>
    public required string Collection { get; init; }

    /// <summary>Timestamp of the last manifest write (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Embedding profile the collection was indexed with. Collection-wide by
    /// design: vectors produced by different embedding models must never coexist
    /// in one collection (plan §3.2).
    /// </summary>
    public required ManifestEmbeddingProfile Embedding { get; init; }

    /// <summary>Per-source entries, keyed by source id (ordinal).</summary>
    public Dictionary<string, ManifestSourceEntry> Sources { get; init; } = [];
}

/// <summary>
/// Triplet identifying the embedding provider that produced a collection's
/// vectors. Any mismatch with the active provider is a hard failure unless the
/// caller explicitly opts into a full reindex.
/// </summary>
public sealed record ManifestEmbeddingProfile
{
    /// <summary>Embedding provider name (<c>IEmbeddingProvider.Name</c>).</summary>
    public required string Provider { get; init; }

    /// <summary>Embedding model identifier (<c>IEmbeddingProvider.Model</c>).</summary>
    public required string Model { get; init; }

    /// <summary>Vector dimensions (<c>IEmbeddingProvider.Dimensions</c>).</summary>
    public int Dimensions { get; init; }
}

/// <summary>State of one ingested source, as recorded in the manifest.</summary>
public sealed record ManifestSourceEntry
{
    /// <summary>
    /// SHA-256 (lowercase hex) of the source's loaded content — the change
    /// detector of incremental ingestion.
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>Timestamp of the source's last ingestion (UTC).</summary>
    public DateTimeOffset IngestedAt { get; init; }

    /// <summary>Chunker configuration used for this source.</summary>
    public required ManifestChunkerProfile Chunker { get; init; }
}

/// <summary>
/// Chunker configuration recorded per source: any difference with the current
/// configuration (name, version, or options) re-ingests the source.
/// </summary>
public sealed record ManifestChunkerProfile
{
    /// <summary>Chunking strategy name (<c>IChunkingStrategy.Name</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Chunking algorithm version (<c>IChunkingStrategy.Version</c>).</summary>
    public required string Version { get; init; }

    /// <summary>Maximum chunk size, in characters.</summary>
    public int MaxChunkSize { get; init; }

    /// <summary>Overlap between consecutive chunks, in characters.</summary>
    public int Overlap { get; init; }

    /// <summary>Strategy-specific extension knobs.</summary>
    public Dictionary<string, string> Extensions { get; init; } = [];

    /// <summary>Ordinal equality on name, version, sizes, and every extension pair.</summary>
    public bool Matches(ManifestChunkerProfile other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!string.Equals(Name, other.Name, StringComparison.Ordinal)
            || !string.Equals(Version, other.Version, StringComparison.Ordinal)
            || MaxChunkSize != other.MaxChunkSize
            || Overlap != other.Overlap
            || Extensions.Count != other.Extensions.Count)
        {
            return false;
        }

        foreach (var (key, value) in Extensions)
        {
            if (!other.Extensions.TryGetValue(key, out var otherValue)
                || !string.Equals(value, otherValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
