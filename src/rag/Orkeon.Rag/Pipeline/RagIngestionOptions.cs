namespace Orkeon.Rag.Pipeline;

/// <summary>
/// Options of the <see cref="DefaultIngestionPipeline"/>.
/// Bound from configuration section <c>Orkeon:Rag:Ingestion</c> by
/// <c>AddOrkeonRag</c>.
/// </summary>
public sealed record RagIngestionOptions
{
    /// <summary>
    /// Chunking strategy resolved by the chunking factory when an
    /// <see cref="Abstractions.Models.IngestionRequest"/> names none.
    /// Defaults to <c>recursive</c> (consolidated strategy names, plan §4.3).
    /// </summary>
    public string DefaultChunkingStrategy { get; init; } = "recursive";
}
