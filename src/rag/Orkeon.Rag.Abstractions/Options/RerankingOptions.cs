namespace Orkeon.Rag.Abstractions.Options;

/// <summary>
/// Reranking stage configuration. Holds the default 50 → 5 cascade constants
/// (guide §7): <see cref="CandidateK"/> candidates are retrieved wide, then the
/// selected <see cref="Reranker"/> truncates them to the best <see cref="TopN"/>.
/// Consumed by the pipeline at the integration batch (RAG-04/C4).
/// </summary>
public sealed record RerankingOptions
{
    /// <summary>Default number of candidates fed to the reranker (wide stage of the cascade).</summary>
    public const int DefaultCandidateK = 50;

    /// <summary>Default number of chunks kept after reranking (narrow stage of the cascade).</summary>
    public const int DefaultTopN = 5;

    /// <summary>
    /// Name or alias of the reranker resolved through the reranker factory
    /// (e.g. <c>none</c>/<c>noop</c>, <c>llm</c>/<c>listwise</c>,
    /// <c>onnx</c>/<c>cross-encoder</c>). Unknown names fail loudly.
    /// </summary>
    public string Reranker { get; init; } = "none";

    /// <summary>Number of candidates retrieved before reranking. Defaults to <see cref="DefaultCandidateK"/>.</summary>
    public int CandidateK { get; init; } = DefaultCandidateK;

    /// <summary>Number of chunks kept after reranking. Defaults to <see cref="DefaultTopN"/>.</summary>
    public int TopN { get; init; } = DefaultTopN;
}
