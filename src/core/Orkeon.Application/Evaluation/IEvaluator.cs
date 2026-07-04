using Orkeon.Domain.SharedKernel;

namespace Orkeon.Application.Evaluation;

/// <summary>
/// Evaluates a single aspect of an agent output.
/// </summary>
public interface IEvaluator
{
    /// <summary>
    /// Short identifier for this evaluator.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable description of what the evaluator measures.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// False for deterministic evaluators, true for LLM-as-Judge evaluators.
    /// </summary>
    bool RequiresLlm { get; }

    /// <summary>
    /// Evaluates the given input and produces a score.
    /// </summary>
    System.Threading.Tasks.Task<EvaluationScore> EvaluateAsync(EvaluationInput input, CancellationToken ct = default);
}

/// <summary>
/// Input data for an evaluation.
/// </summary>
public record EvaluationInput(
    string Output,
    string? ExpectedOutput = null,
    string? TaskDescription = null,
    string? Context = null,
    OutputFormat? ExpectedFormat = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Result of a single evaluation.
/// </summary>
public record EvaluationScore(
    string EvaluatorName,
    double Score,
    string? Reasoning = null,
    IReadOnlyDictionary<string, object>? Details = null)
{
    /// <summary>
    /// Returns true when the score meets or exceeds the given threshold.
    /// </summary>
    public bool IsPassing(double threshold = 0.5) => Score >= threshold;
}
