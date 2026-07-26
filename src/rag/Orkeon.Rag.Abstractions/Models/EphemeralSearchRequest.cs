using System.Collections.Immutable;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// Request handled by an <see cref="Interfaces.IEphemeralCollectionSearch"/>:
/// a set of sources to search semantically, backed by an auto-ingested
/// ephemeral collection (RAG-03/C5). The collection name is derived
/// deterministically from the source identities and chunking options, so the
/// same corpus always maps to the same collection and the incremental
/// ingestion manifest guarantees that an unchanged corpus is never re-embedded.
/// </summary>
public sealed record EphemeralSearchRequest
{
    /// <summary>Sources forming the corpus to search (files, inline text, URLs…).</summary>
    public required ImmutableList<SourceDescriptor> Sources { get; init; }

    /// <summary>Natural-language query.</summary>
    public required string Query { get; init; }

    /// <summary>
    /// Number of scored candidates to retrieve, best first. Callers applying
    /// their own similarity cut-off should request more candidates than they
    /// intend to keep. Defaults to 50 (the candidate stage of the rerank cascade).
    /// </summary>
    public int TopK { get; init; } = 50;

    /// <summary>
    /// Chunking strategy name resolved by the chunking factory
    /// (e.g. <c>recursive</c>, <c>structural</c>). <c>null</c> selects the
    /// ingestion pipeline default.
    /// </summary>
    public string? ChunkingStrategy { get; init; }

    /// <summary>Chunking parameters passed to the selected strategy.</summary>
    public ChunkingOptions Chunking { get; init; } = new();

    /// <summary>
    /// Optional caller label folded into the collection name (e.g. the tool
    /// name) so distinct callers over the same corpus stay isolated.
    /// </summary>
    public string? CollectionPrefix { get; init; }
}
