using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// End-user question handled by the <see cref="Interfaces.IRagPipeline"/> façade.
/// </summary>
public sealed record RagQuery
{
    /// <summary>The question text.</summary>
    public required string Text { get; init; }

    /// <summary>Collection to query in the document store.</summary>
    public required string Collection { get; init; }

    /// <summary>
    /// Number of chunks kept for context assembly. Defaults to the 5-result stage
    /// of the 50 → 5 rerank cascade (guide §7.3).
    /// </summary>
    public int TopN { get; init; } = 5;

    /// <summary>Optional metadata filters applied at retrieval.</summary>
    public ImmutableDictionary<string, string> Filters { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
