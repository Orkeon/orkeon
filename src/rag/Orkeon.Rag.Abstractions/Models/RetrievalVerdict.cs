using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// CRAG verdict produced by an <see cref="Interfaces.IRetrievalEvaluator"/>:
/// an overall grade plus optional per-chunk relevance.
/// </summary>
public sealed record RetrievalVerdict
{
    /// <summary>Overall grade of the retrieved chunk set for the query.</summary>
    public required RetrievalGrade Grade { get; init; }

    /// <summary>Optional per-chunk relevance judgements (decompose-then-recompose input).</summary>
    public ImmutableList<ChunkRelevance> ChunkRelevances { get; init; } =
        ImmutableList<ChunkRelevance>.Empty;

    /// <summary>Optional evaluator rationale (kept in the trace for debugging/eval).</summary>
    public string? Rationale { get; init; }
}

/// <summary>
/// Relevance judgement of a single chunk for a query, emitted alongside a
/// <see cref="RetrievalVerdict"/>.
/// </summary>
public sealed record ChunkRelevance
{
    /// <summary>Identifier of the judged chunk.</summary>
    public required string ChunkId { get; init; }

    /// <summary>Relevance in [0, 1], higher is more relevant.</summary>
    public required double Relevance { get; init; }
}
