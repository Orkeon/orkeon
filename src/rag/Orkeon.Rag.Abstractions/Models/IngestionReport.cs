using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// Outcome of an <see cref="Interfaces.IIngestionPipeline"/> run.
/// </summary>
public sealed record IngestionReport
{
    /// <summary>Collection the run targeted.</summary>
    public required string Collection { get; init; }

    /// <summary>Number of documents loaded from the requested sources.</summary>
    public int DocumentsLoaded { get; init; }

    /// <summary>Number of chunks produced by the chunking stage.</summary>
    public int ChunksCreated { get; init; }

    /// <summary>Number of chunks embedded and upserted.</summary>
    public int ChunksEmbedded { get; init; }

    /// <summary>Number of chunks skipped (e.g. unchanged since the last ingestion).</summary>
    public int ChunksSkipped { get; init; }

    /// <summary>Total wall-clock duration of the run.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Non-fatal errors encountered during the run (empty when clean).</summary>
    public ImmutableList<string> Errors { get; init; } = ImmutableList<string>.Empty;
}
