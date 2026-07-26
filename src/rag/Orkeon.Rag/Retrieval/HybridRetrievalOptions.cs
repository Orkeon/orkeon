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
    /// Gets or sets a value indicating whether hybrid retrieval is enabled (opt-in,
    /// default <see langword="false"/>). When enabled, <c>AddOrkeonRag</c> wraps the
    /// registered <c>IDocumentStore</c> in a <see cref="HybridSearchDocumentStore"/>.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the Reciprocal Rank Fusion constant used by the emulated hybrid path
    /// (must be positive). Defaults to the standard
    /// <see cref="ReciprocalRankFusion.DefaultK"/> (60).
    /// </summary>
    public int RrfK { get; set; } = ReciprocalRankFusion.DefaultK;
}
