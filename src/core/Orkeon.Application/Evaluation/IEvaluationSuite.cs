namespace Orkeon.Application.Evaluation;

/// <summary>
/// A collection of evaluators that can be run together.
/// </summary>
public interface IEvaluationSuite
{
    /// <summary>
    /// Name of this evaluation suite.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The evaluators in this suite.
    /// </summary>
    IReadOnlyList<IEvaluator> Evaluators { get; }

    /// <summary>
    /// Runs all evaluators on each input and produces a report.
    /// </summary>
    System.Threading.Tasks.Task<EvaluationReport> RunAsync(IReadOnlyList<EvaluationInput> inputs, CancellationToken ct = default);

    /// <summary>
    /// Returns a new suite with the given evaluator added.
    /// </summary>
    IEvaluationSuite AddEvaluator(IEvaluator evaluator);
}

/// <summary>
/// Full evaluation report for a suite run.
/// </summary>
public record EvaluationReport(
    string SuiteName,
    DateTime GeneratedAt,
    IReadOnlyList<EvaluationCaseResult> Results,
    EvaluationSummary Summary);

/// <summary>
/// Results for a single evaluation case (one input).
/// </summary>
public record EvaluationCaseResult(
    int CaseIndex,
    string? CaseDescription,
    IReadOnlyList<EvaluationScore> Scores,
    double AverageScore);

/// <summary>
/// Aggregate summary across all cases.
/// </summary>
public record EvaluationSummary(
    int TotalCases,
    double OverallScore,
    IReadOnlyDictionary<string, double> ScoresByEvaluator,
    IReadOnlyDictionary<string, double> Variance);
