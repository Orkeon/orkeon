using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// Flagship result DTO of the <see cref="Interfaces.IRagPipeline"/>: the generated
/// text, its citations (chunk provenance with scores and offsets), and the
/// execution trace.
/// </summary>
public sealed record RagAnswer
{
    /// <summary>Generated answer text, with <c>[n]</c> citation markers.</summary>
    public required string Text { get; init; }

    /// <summary>Citations resolving the <c>[n]</c> markers to their source chunks.</summary>
    public ImmutableList<Citation> Citations { get; init; } = ImmutableList<Citation>.Empty;

    /// <summary>Execution trace of the pipeline run.</summary>
    public RagTrace Trace { get; init; } = new();

    /// <summary>
    /// Groundedness verdict of the optional verification stage;
    /// <c>null</c> when the stage did not run (disabled, or no checker
    /// registered — the trace says which).
    /// </summary>
    public GroundednessResult? Groundedness { get; init; }
}
