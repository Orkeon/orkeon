using System.Globalization;
using System.Text;
using Orkeon.Application.Evaluation;
using Orkeon.Domain.Common;

namespace Orkeon.Infrastructure.Evaluation;

/// <summary>
/// Exports benchmark and comparison reports to markdown format.
/// </summary>
public static class ReportExporter
{
    /// <summary>
    /// Exports a BenchmarkReport to a formatted markdown string.
    /// </summary>
    public static string ToMarkdown(BenchmarkReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Benchmark Report: {report.DatasetName}");
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"- **Generated**: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC"));
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Runs per case**: {report.RunsPerCase}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Total cases**: {report.Results.Count}");
        sb.AppendLine();

        // Summary
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine(Inv.Format($"- **Overall Mean**: {report.Summary.OverallMean:F4}"));
        sb.AppendLine(Inv.Format($"- **Overall Std Dev**: {report.Summary.OverallStdDev:F4}"));
        sb.AppendLine();

        // Per-evaluator table
        if (report.Summary.MeanByEvaluator.Count > 0)
        {
            sb.AppendLine("### Scores by Evaluator");
            sb.AppendLine();
            sb.AppendLine("| Evaluator | Mean | Variance |");
            sb.AppendLine("|-----------|------|----------|");

            foreach (var (name, mean) in report.Summary.MeanByEvaluator)
            {
                var variance = report.Summary.VarianceByEvaluator.GetValueOrDefault(name, 0.0);
                sb.AppendLine(Inv.Format($"| {name} | {mean:F4} | {variance:F6} |"));
            }

            sb.AppendLine();
        }

        // Per-case details
        if (report.Results.Count > 0)
        {
            sb.AppendLine("## Case Details");
            sb.AppendLine();

            foreach (var caseResult in report.Results)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"### Case {caseResult.CaseIndex}");
                sb.AppendLine();

                if (caseResult.MeanScores.Count > 0)
                {
                    sb.AppendLine("| Evaluator | Mean | Std Dev |");
                    sb.AppendLine("|-----------|------|---------|");

                    foreach (var (name, mean) in caseResult.MeanScores)
                    {
                        var stdDev = caseResult.StdDevScores.GetValueOrDefault(name, 0.0);
                        sb.AppendLine(Inv.Format($"| {name} | {mean:F4} | {stdDev:F4} |"));
                    }

                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Exports a ComparisonResult to a formatted markdown string.
    /// </summary>
    public static string ToMarkdown(ComparisonResult comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        var sb = new StringBuilder();
        sb.AppendLine("# Benchmark Comparison");
        sb.AppendLine();

        var status = comparison.HasRegression ? "REGRESSION DETECTED" : "No Regression";
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Status**: {status}");
        sb.AppendLine();
        sb.AppendLine(Inv.Format($"- **Baseline Score**: {comparison.BaselineScore:F4}"));
        sb.AppendLine(Inv.Format($"- **Current Score**: {comparison.CurrentScore:F4}"));
        sb.AppendLine(Inv.Format($"- **Delta**: {comparison.ScoreDelta:+0.0000;-0.0000;0.0000}"));
        sb.AppendLine();

        if (comparison.EvaluatorComparisons.Count > 0)
        {
            sb.AppendLine("## Per-Evaluator Comparison");
            sb.AppendLine();
            sb.AppendLine("| Evaluator | Baseline | Current | Delta | Status |");
            sb.AppendLine("|-----------|----------|---------|-------|--------|");

            foreach (var eval in comparison.EvaluatorComparisons)
            {
                string evalStatus;
                if (eval.IsRegression)
                    evalStatus = "Regression";
                else if (eval.IsImprovement)
                    evalStatus = "Improvement";
                else
                    evalStatus = "Stable";
                sb.AppendLine(Inv.Format($"| {eval.EvaluatorName} | {eval.BaselineScore:F4} | {eval.CurrentScore:F4} | {eval.Delta:+0.0000;-0.0000;0.0000} | {evalStatus} |"));
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
