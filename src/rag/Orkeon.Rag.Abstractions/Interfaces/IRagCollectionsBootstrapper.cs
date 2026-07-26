using Orkeon.Domain.Configuration;

namespace Orkeon.Rag.Abstractions.Interfaces;

/// <summary>
/// Prepares the RAG collections declared by a crew configuration (YAML <c>rag:</c> block)
/// before the crew runs: each declared collection is ingested through the incremental
/// ingestion pipeline, so a fresh manifest makes the whole call a cheap no-op
/// (0 embeddings computed) while new or modified sources are (re-)ingested.
/// </summary>
/// <remarks>
/// Registered by <c>AddOrkeonRag</c>; consumers resolve it optionally so hosts without
/// the RAG subsystem are unaffected. An embedding-model drift surfaces as the ingestion
/// pipeline's hard failure — never a silent re-index (RAG-03/C1).
/// </remarks>
public interface IRagCollectionsBootstrapper
{
    /// <summary>
    /// Ingests every collection declared in <paramref name="config"/> (no-op when the
    /// collection's manifest is up to date).
    /// </summary>
    /// <param name="config">The crew-level RAG configuration (collections + sources + chunking).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task PrepareAsync(RagCrewConfig config, CancellationToken cancellationToken = default);
}
