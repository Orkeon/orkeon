namespace Orkeon.Domain.Configuration;

/// <summary>
/// Typed model of the crew-level <c>rag:</c> YAML block (RAG-03/C4, plan §8.2): the
/// knowledge collections with their ingestion sources, ingested when the crew is created,
/// and the crew-wide retrieval defaults. The document store itself is the host's choice
/// (<c>Orkeon:Rag:Provider</c>), not the crew's. Nothing here triggers I/O.
/// </summary>
public sealed record RagCrewConfig
{
    /// <summary>Gets the declared collections keyed by collection name (empty when none declared).</summary>
    public IReadOnlyDictionary<string, RagCollectionConfig> Collections { get; init; }
        = new Dictionary<string, RagCollectionConfig>(StringComparer.Ordinal);

    /// <summary>
    /// Gets the default retrieval profile applied to knowledge attachments that do not
    /// declare their own (<c>rag.defaults.profile</c>), or null for the host default
    /// (<c>Orkeon:Rag:Profile</c>). The crew factory copies it onto those attachments.
    /// </summary>
    public string? DefaultProfile { get; init; }

    /// <summary>
    /// Gets the virtual folder the crew was read from — the one a relative source
    /// (<c>./data/faq.txt</c>) resolves against: <c>/crew</c> under <c>orkeon run crew.yaml</c>,
    /// the directory itself for a crew directory. The YAML loader sets it when it reads a file or
    /// a directory; null for a crew read from a string or built in code, whose relative sources
    /// are then refused with a warning (GAP-27).
    /// </summary>
    public string? CrewDirectory { get; init; }
}

/// <summary>
/// A single declared RAG collection (<c>rag.collections.&lt;name&gt;</c>): its ingestion
/// sources (virtual paths, globs or directories, expanded at crew creation) and optional
/// chunking settings.
/// </summary>
public sealed record RagCollectionConfig
{
    /// <summary>
    /// Gets the ingestion sources as written: virtual paths, glob patterns (<c>*</c>, <c>**</c>,
    /// <c>?</c>), directories (every file below them), http(s) URLs, or paths relative to
    /// <see cref="RagCrewConfig.CrewDirectory"/>. Empty = query-only collection.
    /// </summary>
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
