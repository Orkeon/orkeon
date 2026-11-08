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

    private static readonly System.Text.Json.JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        MaxDepth = Orkeon.Domain.Constants.Serialization.SerializationDefaults.JsonMaxDepth,
    };

    /// <summary>Exports an <see cref="EvaluationReport"/> as indented JSON with snake_case property names.</summary>
    public static string ToJson(EvaluationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return System.Text.Json.JsonSerializer.Serialize(report, s_json);
    }

    /// <summary>Exports a <see cref="BenchmarkReport"/> as indented JSON with snake_case property names.</summary>
    public static string ToJson(BenchmarkReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return System.Text.Json.JsonSerializer.Serialize(report, s_json);
    }

    /// <summary>Exports a <see cref="ComparisonResult"/> as indented JSON with snake_case property names.</summary>
    public static string ToJson(ComparisonResult comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return System.Text.Json.JsonSerializer.Serialize(comparison, s_json);
    }

    /// <summary>
    /// Reads a report written by <see cref="ToJson(BenchmarkReport)"/> or by <see cref="ToJson(EvaluationReport)"/>
    /// as the benchmark report <see cref="CompareReports"/> takes (an evaluation report becomes a one-run benchmark).
    /// An evaluation report names its dataset in a top-level <c>dataset</c> string (<c>orkeon eval run</c> writes
    /// it): the benchmark takes that name, never the suite's.
    /// </summary>
    /// <exception cref="InvalidOperationException">The JSON is neither report, or an evaluation report names no dataset.</exception>
    public static BenchmarkReport ReadComparableReport(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("dataset_name", out _))
                {
                    return System.Text.Json.JsonSerializer.Deserialize<BenchmarkReport>(json, s_json)
                           ?? throw new InvalidOperationException("The benchmark report is empty.");
                }

                if (doc.RootElement.TryGetProperty("suite_name", out _))
                {
                    if (!doc.RootElement.TryGetProperty("dataset", out var dataset)
                        || dataset.ValueKind != System.Text.Json.JsonValueKind.String
                        || string.IsNullOrWhiteSpace(dataset.GetString()))
                    {
                        throw new InvalidOperationException("The evaluation report names no 'dataset': it cannot be compared with another.");
                    }

                    var evaluation = System.Text.Json.JsonSerializer.Deserialize<EvaluationReport>(json, s_json)
                                     ?? throw new InvalidOperationException("The evaluation report is empty.");
                    return new BenchmarkReport(
                        dataset.GetString()!,
                        evaluation.GeneratedAt,
                        1,
                        [],
                        new BenchmarkSummary(
                            evaluation.Summary.OverallScore,
                            0,
                            evaluation.Summary.ScoresByEvaluator,
                            evaluation.Summary.Variance));
                }
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException($"Not a JSON report: {ex.Message}", ex);
        }

        throw new InvalidOperationException("Not an evaluation or benchmark report: expected a JSON object with 'suite_name' or 'dataset_name'.");
    }
}
