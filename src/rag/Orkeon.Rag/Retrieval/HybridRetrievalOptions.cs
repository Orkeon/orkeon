namespace Orkeon.Rag.Retrieval;

/// <summary>
/// Hybrid-retrieval options (RAG-04/C2), bound from the
/// <c>Orkeon:Rag:Retrieval:Hybrid</c> configuration section. The shorthand flat value
/// <c>Orkeon:Rag:Retrieval:Hybrid = true</c> is also accepted and maps to
/// <see cref="Enabled"/>.
/// </summary>
public sealed class HybridRetrievalOptions
{
    /// <summary>
    /// Gets or sets the <b>default</b> search mode of the
    /// <see cref="HybridSearchDocumentStore"/> decorator (which <c>AddOrkeonRag</c>
    /// always installs since RAG-04/C4 so ingestion feeds the BM25 index): when
    /// <see langword="false"/> (the default) searches pass through to the inner
    /// store verbatim unless the query sets
    /// <c>RetrievalQuery.Hybrid = true</c> (as the <c>balanced</c>/<c>quality</c>
    /// profiles do); when <see langword="true"/> searches fuse by default.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the Reciprocal Rank Fusion constant used by the emulated hybrid path
    /// (must be positive). Defaults to the standard
    /// <see cref="ReciprocalRankFusion.DefaultK"/> (60).
    /// </summary>
    public int RrfK { get; set; } = ReciprocalRankFusion.DefaultK;
}
