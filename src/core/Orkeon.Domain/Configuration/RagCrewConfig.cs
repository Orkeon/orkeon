namespace Orkeon.Domain.Configuration;

/// <summary>
/// Typed model of the crew-level <c>rag:</c> YAML block (RAG-03/C4, plan §8.2).
/// Declares the memory/vector provider, the knowledge collections with their ingestion
/// sources, and crew-wide retrieval defaults. Parsing-only at this stage: kickoff-time
/// ingestion consumes this configuration in a later lot — nothing here triggers I/O.
/// </summary>
public sealed record RagCrewConfig
{
    /// <summary>
    /// Gets the memory/vector store provider name used for the RAG collections
    /// (e.g. "InMemory", "Redis", "Sqlite", "ChromaDb", "Pinecone", "LanceDb"),
    /// or null to fall back to the crew's memory provider / global default.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>Gets the declared collections keyed by collection name (empty when none declared).</summary>
    public IReadOnlyDictionary<string, RagCollectionConfig> Collections { get; init; }
        = new Dictionary<string, RagCollectionConfig>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the default query profile applied to knowledge attachments that do not
    /// declare their own (<c>rag.defaults.profile</c>), or null for the global default.
    /// </summary>
    public string? DefaultProfile { get; init; }
}

/// <summary>
/// A single declared RAG collection (<c>rag.collections.&lt;name&gt;</c>): its ingestion
/// sources (file globs / directories, resolved at kickoff) and optional chunking settings.
/// </summary>
public sealed record RagCollectionConfig
{
    /// <summary>Gets the ingestion source patterns (file globs or directories). Empty = query-only collection.</summary>
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();

    /// <summary>Gets the chunking configuration for ingestion, or null for the pipeline defaults.</summary>
    public RagChunkingConfig? Chunking { get; init; }
}

/// <summary>
/// Chunking settings for a RAG collection (<c>rag.collections.&lt;name&gt;.chunking</c>).
/// </summary>
public sealed record RagChunkingConfig
{
    /// <summary>Gets the chunking strategy name (e.g. "recursive"). Default: "recursive".</summary>
    public string Strategy { get; init; } = "recursive";

    /// <summary>Gets the maximum tokens per chunk. Default: 512.</summary>
    public int MaxTokens { get; init; } = 512;

    /// <summary>Gets the token overlap between consecutive chunks. Default: 64.</summary>
    public int Overlap { get; init; } = 64;
}
