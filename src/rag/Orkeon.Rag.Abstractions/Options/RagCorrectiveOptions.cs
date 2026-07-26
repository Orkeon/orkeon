namespace Orkeon.Rag.Abstractions.Options;

/// <summary>
/// Corrective-RAG (CRAG) options (RAG-06, guide §8). Bound from
/// <c>Orkeon:Rag:Corrective</c>; consumed by the corrective graph pipeline built
/// on the Graph orchestration mode (<c>StateGraph&lt;RagGraphState&gt;</c>).
/// </summary>
/// <remarks>
/// The corrective loop (rewrite → retrieve → evaluate, and the groundedness
/// re-loop) is bounded twice: by <see cref="MaxIterations"/> here, and by the
/// circuit breaker native to the Graph mode. At exhaustion the pipeline
/// generates with the best chunks available and traces the exhaustion — it
/// never throws and never loops forever.
/// </remarks>
public sealed class RagCorrectiveOptions
{
    /// <summary>Default corrective-iteration budget.</summary>
    public const int DefaultMaxIterations = 3;

    /// <summary>
    /// Maximum number of corrective iterations (query rewrites — whether
    /// triggered by an <c>Incorrect</c> retrieval verdict or by an ungrounded
    /// answer) before the pipeline generates with the best chunks available.
    /// Defaults to <see cref="DefaultMaxIterations"/>.
    /// </summary>
    public int MaxIterations { get; set; } = DefaultMaxIterations;

    /// <summary>Opt-in web fallback fired after query rewriting is exhausted.</summary>
    public RagWebFallbackOptions WebFallback { get; set; } = new();
}

/// <summary>
/// Web-fallback node of <see cref="RagCorrectiveOptions"/>: an opt-in last
/// resort fired only after query rewriting is exhausted, and only when a web
/// document retriever is registered — otherwise the edge is skipped and traced.
/// </summary>
public sealed class RagWebFallbackOptions
{
    /// <summary>Default number of web documents requested by the fallback.</summary>
    public const int DefaultMaxResults = 3;

    /// <summary>
    /// Whether the web fallback may fire. Off by default (opt-in): retrieval
    /// stays strictly local unless the host explicitly enables it AND registers
    /// a web document retriever.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Maximum number of web documents requested from the retriever. Defaults
    /// to <see cref="DefaultMaxResults"/>.
    /// </summary>
    public int MaxResults { get; set; } = DefaultMaxResults;
}
