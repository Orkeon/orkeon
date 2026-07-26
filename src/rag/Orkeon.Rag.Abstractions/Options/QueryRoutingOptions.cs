namespace Orkeon.Rag.Abstractions.Options;

/// <summary>
/// Adaptive-RAG query-routing configuration (guide §8.4, RAG-05/C3), bound from
/// the <c>Orkeon:Rag:QueryRouting</c> configuration section. Selects the
/// <see cref="Interfaces.IQueryComplexityClassifier"/> implementation used to
/// route queries (<c>NoRetrieval</c> / <c>SingleShot</c> / <c>Iterative</c>).
/// </summary>
/// <remarks>
/// Properties are mutable on purpose: instances are completed by
/// <c>IConfiguration.Bind</c> (same convention as <see cref="RagOptions"/>).
/// </remarks>
public sealed class QueryRoutingOptions
{
    /// <summary>Deterministic rule-based classifier — zero LLM call (default).</summary>
    public const string HeuristicClassifier = "heuristic";

    /// <summary>Constrained lightweight LLM classifier (one chat call per query).</summary>
    public const string LlmClassifier = "llm";

    /// <summary>
    /// Classifier name: <see cref="HeuristicClassifier"/> (default — safe,
    /// deterministic, works without any LLM) or <see cref="LlmClassifier"/>.
    /// Unknown names fail loudly at resolution. When <c>llm</c> is selected but
    /// no <c>IChatClient</c> is registered, the heuristic classifier is used as
    /// the documented fallback (with a warning) rather than failing.
    /// </summary>
    public string Classifier { get; set; } = HeuristicClassifier;
}
