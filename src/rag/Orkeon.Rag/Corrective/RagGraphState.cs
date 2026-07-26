using System.Collections.Immutable;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Corrective;

/// <summary>
/// Immutable state flowing through the corrective RAG graph
/// (<c>StateGraph&lt;RagGraphState&gt;</c>, RAG-06/C1): the original query, the
/// current rewritten probe, the corrective iteration count, the retained chunks,
/// the last retrieval verdict, the generated answer, its groundedness, and the
/// accumulated trace. Every node returns a new instance (<c>with</c> mutations).
/// </summary>
public sealed record RagGraphState
{
    /// <summary>The original user query. Generation and citations always use it.</summary>
    public required RagQuery Query { get; init; }

    /// <summary>
    /// Current rewritten retrieval probe produced by the <c>rewrite_query</c>
    /// node; <c>null</c> until the first rewrite (the original text is probed).
    /// </summary>
    public string? RewrittenQuery { get; init; }

    /// <summary>Number of corrective iterations (query rewrites) executed so far.</summary>
    public int Iteration { get; init; }

    /// <summary>Chunks currently retained for evaluation/refinement/generation, best first.</summary>
    public ImmutableList<ScoredChunk> Chunks { get; init; } = ImmutableList<ScoredChunk>.Empty;

    /// <summary>Last verdict emitted by the <c>evaluate</c> node; <c>null</c> before it runs.</summary>
    public RetrievalVerdict? Verdict { get; init; }

    /// <summary>All verdicts emitted so far, in order (feeds <see cref="RagTrace.Verdicts"/>).</summary>
    public ImmutableList<RetrievalVerdict> Verdicts { get; init; } =
        ImmutableList<RetrievalVerdict>.Empty;

    /// <summary>Generated answer text; <c>null</c> until the <c>generate</c> node runs.</summary>
    public string? Answer { get; init; }

    /// <summary>Groundedness verdict of the answer; <c>null</c> when the check did not run.</summary>
    public GroundednessResult? Groundedness { get; init; }

    /// <summary>Accumulated trace steps (<c>corrective:&lt;node&gt;</c> names), in execution order.</summary>
    public ImmutableList<RagTraceStep> Steps { get; init; } = ImmutableList<RagTraceStep>.Empty;

    /// <summary>Rewritten probe texts produced so far (feeds <see cref="RagTrace.QueryVariants"/>).</summary>
    public ImmutableList<string> RewrittenQueries { get; init; } = ImmutableList<string>.Empty;

    /// <summary>Whether the <c>web_fallback</c> node already fired (it fires at most once).</summary>
    public bool WebFallbackAttempted { get; init; }

    /// <summary>
    /// Whether the corrective budget is exhausted: the pipeline is on its
    /// best-effort path (generate with the best chunks available, no more loops).
    /// </summary>
    public bool Exhausted { get; init; }

    /// <summary>
    /// Routing decision computed by the decision nodes (<c>evaluate</c>,
    /// <c>check_groundedness</c>) and read by their conditional edges.
    /// </summary>
    public string NextNode { get; init; } = "";

    /// <summary>The retrieval probe of the current iteration: the last rewrite, or the original text.</summary>
    public string CurrentQueryText => RewrittenQuery ?? Query.Text;
}
