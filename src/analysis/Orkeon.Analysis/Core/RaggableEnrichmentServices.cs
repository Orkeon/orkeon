using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Analysis.Core;

/// <summary>
/// Optional enrichment and persistence collaborators shared by <see cref="RaggableTreeBuilder"/>
/// and <see cref="IncrementalReindexEngine"/>. Each is independent of the core parse/extract
/// pipeline: framework fingerprinters tag nodes, the summarizer adds LLM descriptions, the
/// embedder produces vectors, and the vector store persists them. All are optional — an empty
/// instance (<see cref="None"/>) yields the plain structural index with no enrichment.
/// </summary>
public sealed record RaggableEnrichmentServices
{
    /// <summary>Framework fingerprinters applied to nodes/edges after dependency resolution.</summary>
    public IEnumerable<IFrameworkFingerprinter>? Fingerprinters { get; init; }

    /// <summary>Optional LLM summarizer that populates semantic summaries.</summary>
    public INodeSummarizer? Summarizer { get; init; }

    /// <summary>Optional embedding provider that produces vectors for eligible nodes.</summary>
    public IEmbeddingProvider? Embedder { get; init; }

    /// <summary>Optional vector store that persists embedded nodes.</summary>
    public IVectorStoreProvider? VectorStore { get; init; }

    /// <summary>An instance with no enrichment collaborators configured.</summary>
    public static RaggableEnrichmentServices None { get; } = new();
}
