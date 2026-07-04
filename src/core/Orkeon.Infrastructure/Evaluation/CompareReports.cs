using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Evaluation;

/// <summary>
/// Compares two benchmark reports and detects regressions or improvements.
/// </summary>
public static class CompareReports
{
    /// <summary>
    /// Compares a current benchmark report against a baseline.
    /// </summary>
    /// <param name="baseline">The baseline (previous) benchmark report.</param>
    /// <param name="current">The current benchmark report.</param>
    /// <param name="regressionThreshold">
    /// Minimum drop in score (absolute) to consider a regression. Default is 0.05 (5%).
    /// </param>
    public static ComparisonResult Compare(
        BenchmarkReport baseline, BenchmarkReport current,
        double regressionThreshold = 0.05)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var baselineScore = baseline.Summary.OverallMean;
        var currentScore = current.Summary.OverallMean;
        var delta = currentScore - baselineScore;

        var comparisons = new List<EvaluatorComparison>();

        // Collect all evaluator names from both reports
        var evaluatorNames = baseline.Summary.MeanByEvaluator.Keys
            .Union(current.Summary.MeanByEvaluator.Keys)
            .Distinct()
            .ToList();

        foreach (var name in evaluatorNames)
        {
            var baseVal = baseline.Summary.MeanByEvaluator.GetValueOrDefault(name, 0.0);
            var currVal = current.Summary.MeanByEvaluator.GetValueOrDefault(name, 0.0);
            var evalDelta = currVal - baseVal;

            comparisons.Add(new EvaluatorComparison(
                EvaluatorName: name,
                BaselineScore: baseVal,
                CurrentScore: currVal,
                Delta: Math.Round(evalDelta, 4),
                IsRegression: evalDelta < -regressionThreshold,
                IsImprovement: evalDelta > regressionThreshold));
        }

        var hasRegression = comparisons.Any(c => c.IsRegression)
                            || delta < -regressionThreshold;

        return new ComparisonResult(
            HasRegression: hasRegression,
            BaselineScore: baselineScore,
            CurrentScore: currentScore,
            ScoreDelta: Math.Round(delta, 4),
            EvaluatorComparisons: comparisons.AsReadOnly());
    }
}

/// <summary>
/// Result of comparing two benchmark reports.
/// </summary>
public record ComparisonResult(
    bool HasRegression,
    double BaselineScore,
    double CurrentScore,
    double ScoreDelta,
    IReadOnlyList<EvaluatorComparison> EvaluatorComparisons);

/// <summary>
/// Per-evaluator comparison between baseline and current.
/// </summary>
public record EvaluatorComparison(
    string EvaluatorName,
    double BaselineScore,
    double CurrentScore,
    double Delta,
    bool IsRegression,
    bool IsImprovement);
