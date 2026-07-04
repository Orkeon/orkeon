using System.Collections.Immutable;

namespace Orkeon.Application.Interfaces.Rag;

/// <summary>
/// Generic retrieval backend exposed to agents. A concrete implementation may route
/// queries to a RAG pipeline, a RaggableTree index, or any other vector store.
/// </summary>
public interface IRagTool
{
    /// <summary>Executes a retrieval query against the backend selected by <see cref="RagToolQuery.Collection"/>.</summary>
    System.Threading.Tasks.Task<IReadOnlyList<RagSearchHit>> SearchAsync(RagToolQuery query, CancellationToken ct = default);
}

/// <summary>
/// A routed retrieval query. <see cref="Collection"/> selects the backend
/// (e.g. <c>"raggable-tree"</c> for the code index, <c>null</c> for the default pipeline).
/// </summary>
public sealed record RagToolQuery
{
    /// <summary>Query text or natural-language question.</summary>
    public string Text { get; init; } = string.Empty;
    /// <summary>Maximum number of hits to return.</summary>
    public int TopK { get; init; } = 10;
    /// <summary>Backend selector. <c>"raggable-tree"</c> routes to the code index; <c>null</c> uses the default pipeline.</summary>
    public string? Collection { get; init; }
    /// <summary>Optional typed filters passed to the backend (semantics are backend-specific).</summary>
    public ImmutableDictionary<string, object>? Filters { get; init; }
}

/// <summary>
/// A single hit returned by <see cref="IRagTool.SearchAsync"/>.
/// </summary>
public sealed record RagSearchHit
{
    /// <summary>Stable identifier for the source (e.g. FQN or document id).</summary>
    public string SourceId { get; init; } = string.Empty;
    /// <summary>Text content of the hit (snippet, summary, or signature).</summary>
    public string Content { get; init; } = string.Empty;
    /// <summary>Relevance score in [0,1].</summary>
    public float Score { get; init; }
    /// <summary>Optional backend-specific metadata.</summary>
    public ImmutableDictionary<string, object>? Metadata { get; init; }
}
