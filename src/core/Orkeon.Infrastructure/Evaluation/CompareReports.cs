using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Evaluation;

/// <summary>
/// Compares two benchmark reports and detects regressions or improvements.
/// </summary>
public static class CompareReports
{
    /// <summary>
    /// Why <paramref name="baseline"/> and <paramref name="current"/> cannot be compared, or null when they
    /// can: they must score the same dataset with the same evaluators. An evaluator present on one side
    /// alone has no score to compare on the other, and the overall means average different evaluators.
    /// </summary>
    public static string? Incompatibility(BenchmarkReport baseline, BenchmarkReport current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        if (!string.Equals(baseline.DatasetName, current.DatasetName, StringComparison.Ordinal))
            return $"the baseline scored the dataset '{baseline.DatasetName}', the current report '{current.DatasetName}'";

        var baselineEvaluators = baseline.Summary.MeanByEvaluator.Keys.Order(StringComparer.Ordinal).ToList();
        var currentEvaluators = current.Summary.MeanByEvaluator.Keys.Order(StringComparer.Ordinal).ToList();
        if (!baselineEvaluators.SequenceEqual(currentEvaluators, StringComparer.Ordinal))
        {
            return $"the baseline scored the evaluators [{string.Join(", ", baselineEvaluators)}], "
                   + $"the current report [{string.Join(", ", currentEvaluators)}]";
        }

        return null;
    }

    /// <summary>
    /// Compares a current benchmark report against a baseline.
    /// </summary>
    /// <param name="baseline">The baseline (previous) benchmark report.</param>
    /// <param name="current">The current benchmark report.</param>
    /// <param name="regressionThreshold">
    /// Minimum drop in score (absolute) to consider a regression. Default is 0.05 (5%). A finite
    /// number from 0 to 1.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The threshold is negative, above 1 or not a number.</exception>
    /// <exception cref="InvalidOperationException">The reports are not comparable (<see cref="Incompatibility"/>).</exception>
    public static ComparisonResult Compare(
        BenchmarkReport baseline, BenchmarkReport current,
        double regressionThreshold = 0.05)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        if (!IsValidThreshold(regressionThreshold))
            throw new ArgumentOutOfRangeException(nameof(regressionThreshold), regressionThreshold, "The regression threshold must be a number from 0 to 1.");
        if (Incompatibility(baseline, current) is { } reason)
            throw new InvalidOperationException($"The reports cannot be compared: {reason}.");

        var baselineScore = baseline.Summary.OverallMean;
        var currentScore = current.Summary.OverallMean;
        var delta = currentScore - baselineScore;

        var comparisons = new List<EvaluatorComparison>();

        // Both reports score the same evaluators (Incompatibility above).
        var evaluatorNames = baseline.Summary.MeanByEvaluator.Keys.ToList();

        foreach (var name in evaluatorNames)
        {
            var baseVal = baseline.Summary.MeanByEvaluator[name];
            var currVal = current.Summary.MeanByEvaluator[name];
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

    /// <summary>True when <paramref name="threshold"/> is a finite number from 0 to 1.</summary>
    public static bool IsValidThreshold(double threshold) => threshold is >= 0 and <= 1;
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
