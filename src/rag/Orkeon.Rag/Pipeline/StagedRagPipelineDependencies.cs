using Microsoft.Extensions.Logging;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Factories;

namespace Orkeon.Rag.Pipeline;

/// <summary>
/// The optional collaborators of <see cref="StagedRagPipeline"/>: each one backs a
/// stage that <see cref="RagOptions"/> can switch on, so a host only supplies the
/// ones its profile actually needs. Grouped in a single argument so turning a stage
/// on never lengthens the pipeline constructor.
/// </summary>
public sealed record StagedRagPipelineDependencies
{
    /// <summary>
    /// Named query-transformer factory. Required only when
    /// <see cref="RagQueryTransformOptions.Mode"/> is not <c>none</c>.
    /// </summary>
    public QueryTransformerFactory? QueryTransformers { get; init; }

    /// <summary>
    /// Named reranker factory. Required only when
    /// <see cref="RagRerankOptions.Enabled"/> is set.
    /// </summary>
    public RerankerFactory? Rerankers { get; init; }

    /// <summary>
    /// Groundedness checker (RAG-06). Absent while
    /// <see cref="RagGroundednessOptions.Enabled"/> is set, the stage is traced
    /// as skipped.
    /// </summary>
    public IGroundednessChecker? GroundednessChecker { get; init; }

    /// <summary>Logger; absent, the pipeline logs to a no-op logger.</summary>
    public ILogger<StagedRagPipeline>? Logger { get; init; }
}
