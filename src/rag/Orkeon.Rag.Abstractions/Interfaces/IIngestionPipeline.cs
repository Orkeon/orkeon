using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Ingestion facade: loads, chunks, embeds, and upserts sources into a document
/// store collection, incrementally where possible.
/// </summary>
public interface IIngestionPipeline
{
    /// <summary>Runs the ingestion described by <paramref name="request"/>.</summary>
    Task<IngestionReport> IngestAsync(
        IngestionRequest request,
        CancellationToken cancellationToken = default);
}
