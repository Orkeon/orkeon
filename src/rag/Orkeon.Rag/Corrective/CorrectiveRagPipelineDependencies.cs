using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Common.StateMachine;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Options;

namespace Orkeon.Rag.Corrective;

/// <summary>
/// The optional collaborators of <see cref="CorrectiveRagPipeline"/>: the nodes they
/// back are skipped (and traced as skipped) when they are absent, and the graph
/// derives its own circuit-breaker policy unless one is supplied. Grouped in a single
/// argument so wiring an optional node never lengthens the pipeline constructor.
/// </summary>
[Experimental("ORKEXP003", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record CorrectiveRagPipelineDependencies
{
    /// <summary>
    /// Groundedness checker closing the loop; absent, the
    /// <c>check_groundedness</c> node is traced as skipped and the graph ends.
    /// </summary>
    public IGroundednessChecker? GroundednessChecker { get; init; }

    /// <summary>
    /// Web document retriever for the opt-in <c>web_fallback</c> node (requires
    /// <see cref="RagWebFallbackOptions.Enabled"/> too; the edge is skipped and
    /// traced otherwise).
    /// </summary>
    public IWebDocumentRetriever? WebRetriever { get; init; }

    /// <summary>Logger; absent, the pipeline logs to a no-op logger.</summary>
    public ILogger<CorrectiveRagPipeline>? Logger { get; init; }

    /// <summary>
    /// Explicit circuit-breaker override for the graph engine; absent, a policy is
    /// derived from <see cref="RagCorrectiveOptions.MaxIterations"/>.
    /// </summary>
    public CircuitBreakerPolicy? CircuitPolicy { get; init; }
}
