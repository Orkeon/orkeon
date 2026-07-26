namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// Options of one <see cref="Interfaces.IRagEvaluator"/> run: the collection under
/// test, the profile to resolve, the metric cutoff, and the judge selection.
/// </summary>
public sealed record RagEvalOptions
{
    /// <summary>
    /// Profile evaluated when the caller names none. Until <c>RagProfilePresets</c>
    /// land (RAG-04/C4), the default <c>IRagProfileResolver</c> maps every profile
    /// name — including this one — to the single registered pipeline.
    /// </summary>
    public const string DefaultProfile = "default";

    /// <summary>Collection under evaluation in the document store.</summary>
    public required string Collection { get; init; }

    /// <summary>
    /// Profile name resolved through <see cref="Interfaces.IRagProfileResolver"/>
    /// and echoed in the report.
    /// </summary>
    public string Profile { get; init; } = DefaultProfile;

    /// <summary>Cutoff of the retrieval metrics (recall@k, precision@k). Defaults to 5.</summary>
    public int K { get; init; } = 5;

    /// <summary>
    /// <c>TopN</c> passed to the pipeline per query. The evaluator always queries
    /// with <c>max(TopN, K)</c> so the metric cutoff is never starved.
    /// </summary>
    public int TopN { get; init; } = 5;

    /// <summary>
    /// Requests the LLM judge for the generation metrics. Honoured only when an
    /// <c>IChatClient</c> is available — otherwise the run falls back to the
    /// deterministic heuristic judge, and the report says so
    /// (<see cref="RagEvalReport.Judge"/> is never ambiguous).
    /// </summary>
    public bool UseLlmJudge { get; init; }
}
