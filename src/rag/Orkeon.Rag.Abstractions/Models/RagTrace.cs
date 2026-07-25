using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// Execution trace of a <see cref="Interfaces.IRagPipeline"/> run: stages executed,
/// query variants, verdicts, iterations. Feeds debugging, evaluation, and demos.
/// </summary>
public sealed record RagTrace
{
    /// <summary>Pipeline stages executed, in order.</summary>
    public ImmutableList<RagTraceStep> Steps { get; init; } = ImmutableList<RagTraceStep>.Empty;

    /// <summary>Query variants produced by the transform stage (original query excluded).</summary>
    public ImmutableList<string> QueryVariants { get; init; } = ImmutableList<string>.Empty;

    /// <summary>Retrieval verdicts emitted by the corrective loop, in order.</summary>
    public ImmutableList<RetrievalVerdict> Verdicts { get; init; } =
        ImmutableList<RetrievalVerdict>.Empty;

    /// <summary>Number of corrective iterations executed (0 for linear runs).</summary>
    public int Iterations { get; init; }

    /// <summary>Adaptive-RAG routing decision, when a classifier ran.</summary>
    public QueryRoute? Route { get; init; }
}

/// <summary>
/// One executed stage in a <see cref="RagTrace"/>.
/// </summary>
public sealed record RagTraceStep
{
    /// <summary>Stage name (e.g. <c>transform</c>, <c>retrieve</c>, <c>rerank</c>, <c>generate</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Optional human-readable detail for the stage.</summary>
    public string? Detail { get; init; }

    /// <summary>Wall-clock duration of the stage.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Stage-specific data points (counts, chosen component names…).</summary>
    public ImmutableDictionary<string, string> Data { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
