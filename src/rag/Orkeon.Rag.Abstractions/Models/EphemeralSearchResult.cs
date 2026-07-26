namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// Outcome of an <see cref="Interfaces.IEphemeralCollectionSearch"/> run:
/// the scored candidates plus the ingestion report of the backing collection
/// (an unchanged corpus reports zero embedded chunks).
/// </summary>
public sealed record EphemeralSearchResult
{
    /// <summary>Deterministic name of the ephemeral collection that served the search.</summary>
    public required string Collection { get; init; }

    /// <summary>Scored candidates, best first. Scores carry the store's native provenance.</summary>
    public required IReadOnlyList<ScoredChunk> Results { get; init; }

    /// <summary>
    /// Report of the (incremental) ingestion that preceded the search:
    /// <see cref="IngestionReport.SourcesUnchanged"/> counts the sources skipped
    /// with zero embeddings.
    /// </summary>
    public required IngestionReport Ingestion { get; init; }
}
