using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation;

public class ReportExporterTestsFixture
{
    public static BenchmarkReport CreateReport(
        string datasetName,
        DateTime generatedAt,
        int runsPerCase,
        BenchmarkCaseResult[] results,
        BenchmarkSummary summary)
        => new(datasetName, generatedAt, runsPerCase, results, summary);

    public static BenchmarkReport CreateEmptyReport(string datasetName)
        => new(
            DatasetName: datasetName,
            GeneratedAt: DateTime.UtcNow,
            RunsPerCase: 1,
            Results: Array.Empty<BenchmarkCaseResult>(),
            Summary: new BenchmarkSummary(0.0, 0.0,
                new Dictionary<string, double>(),
                new Dictionary<string, double>()));

    public static ComparisonResult CreateComparison(
        bool hasRegression,
        double baselineScore,
        double currentScore,
        double scoreDelta,
        EvaluatorComparison[] evaluatorComparisons)
        => new(hasRegression, baselineScore, currentScore, scoreDelta, evaluatorComparisons);

    public static string ToMarkdown(BenchmarkReport report)
        => ReportExporter.ToMarkdown(report);

    public static string ToMarkdown(ComparisonResult comparison)
        => ReportExporter.ToMarkdown(comparison);
}
