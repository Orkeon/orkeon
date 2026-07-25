using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// Contextual inputs for an <see cref="Interfaces.IQueryTransformer"/> run
/// (Multi-Query, RAG-Fusion, HyDE…).
/// </summary>
public sealed record QueryTransformContext
{
    /// <summary>Target collection, when the transformer wants domain hints.</summary>
    public string? Collection { get; init; }

    /// <summary>Maximum number of query variants to produce (the original query excluded).</summary>
    public int MaxVariants { get; init; } = 3;

    /// <summary>Transformer-specific extension knobs.</summary>
    public ImmutableDictionary<string, string> Extensions { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
