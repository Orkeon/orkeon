namespace Orkeon.Infrastructure.Evaluation;

/// <summary>
/// Configuration options for the evaluation framework.
/// </summary>
public sealed class EvaluationOptions
{
    /// <summary>
    /// When true, LLM-as-Judge evaluators are registered and available.
    /// Requires an IChatClient to be registered in DI. Default: false.
    /// </summary>
    public bool EnableLlmJudge { get; set; }

    /// <summary>
    /// Default number of runs per case for benchmark execution.
    /// </summary>
    public int DefaultRunsPerCase { get; set; } = 3;

    /// <summary>
    /// Absolute score drop threshold (0.0-1.0) to flag a regression.
    /// Default: 0.05 (5% drop).
    /// </summary>
    public double RegressionThreshold { get; set; } = 0.05;
}
