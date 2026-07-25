using System.Collections.Immutable;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// Request handled by an <see cref="Interfaces.IIngestionPipeline"/>: sources to
/// load, chunk, embed, and upsert into a collection.
/// </summary>
public sealed record IngestionRequest
{
    /// <summary>Target collection in the document store.</summary>
    public required string Collection { get; init; }

    /// <summary>Sources to ingest.</summary>
    public required ImmutableList<SourceDescriptor> Sources { get; init; }

    /// <summary>
    /// Chunking strategy name resolved by the chunking factory
    /// (e.g. <c>recursive</c>, <c>sentence</c>, <c>structural</c>, <c>semantic</c>).
    /// <c>null</c> selects the pipeline default.
    /// </summary>
    public string? ChunkingStrategy { get; init; }

    /// <summary>Chunking parameters passed to the selected strategy.</summary>
    public ChunkingOptions Chunking { get; init; } = new();
}
