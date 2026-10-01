namespace Orkeon.Infrastructure.Evaluation;

/// <summary>
/// Configuration options for the evaluation framework.
/// </summary>
public sealed class EvaluationOptions
{
    /// <summary>
    /// When true, the default evaluation suite also runs the LLM-as-Judge evaluators
    /// (coherence, fluency, groundedness). Requires an IChatClient in DI. Default: false.
    /// Bound from the <c>Evaluation</c> section.
    /// </summary>
    public bool EnableLlmJudge { get; set; }
}
