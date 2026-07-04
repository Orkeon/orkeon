using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation;

public class ReportExporterTests
{
    private readonly ReportExporterTestsFixture _fixture = new();

    [Fact]
    public void ShouldGenerateValidMarkdown_WhenExportingBenchmarkReport()
    {
        var report = ReportExporterTestsFixture.CreateReport(
            "TestData",
            new DateTime(2026, 3, 8, 12, 0, 0, DateTimeKind.Utc),
            3,
            [
                new BenchmarkCaseResult(
                    CaseIndex: 0,
                    RunResults: Array.Empty<EvaluationCaseResult>(),
                    MeanScores: new Dictionary<string, double> { ["Eval1"] = 0.85, ["Eval2"] = 0.72 },
                    StdDevScores: new Dictionary<string, double> { ["Eval1"] = 0.05, ["Eval2"] = 0.03 })
            ],
            new BenchmarkSummary(
                OverallMean: 0.785,
                OverallStdDev: 0.04,
                MeanByEvaluator: new Dictionary<string, double> { ["Eval1"] = 0.85, ["Eval2"] = 0.72 },
                VarianceByEvaluator: new Dictionary<string, double> { ["Eval1"] = 0.0025, ["Eval2"] = 0.0009 }));

        var markdown = ReportExporterTestsFixture.ToMarkdown(report);

        Assert.Contains("# Benchmark Report: TestData", markdown);
        Assert.Contains("**Runs per case**: 3", markdown);
        Assert.Contains("**Overall Mean**: 0.7850", markdown);
        Assert.Contains("Eval1", markdown);
        Assert.Contains("Eval2", markdown);
        Assert.Contains("| Evaluator", markdown);
        Assert.Contains("Case 0", markdown);
    }

    [Fact]
    public void ShouldGenerateValidMarkdown_WhenExportingComparisonResult()
    {
        var comparison = ReportExporterTestsFixture.CreateComparison(
            hasRegression: true,
            baselineScore: 0.85,
            currentScore: 0.72,
            scoreDelta: -0.13,
            [
                new EvaluatorComparison("FormatCompliance", 0.9, 0.7, -0.2, IsRegression: true, IsImprovement: false),
                new EvaluatorComparison("TextQuality", 0.8, 0.9, 0.1, IsRegression: false, IsImprovement: true)
            ]);

        var markdown = ReportExporterTestsFixture.ToMarkdown(comparison);

        Assert.Contains("# Benchmark Comparison", markdown);
        Assert.Contains("REGRESSION DETECTED", markdown);
        Assert.Contains("**Baseline Score**: 0.8500", markdown);
        Assert.Contains("**Current Score**: 0.7200", markdown);
        Assert.Contains("FormatCompliance", markdown);
        Assert.Contains("TextQuality", markdown);
        Assert.Contains("Regression", markdown);
        Assert.Contains("Improvement", markdown);
    }

    [Fact]
    public void ShouldShowStableStatus_WhenNoRegressionDetected()
    {
        var comparison = ReportExporterTestsFixture.CreateComparison(
            hasRegression: false,
            baselineScore: 0.8,
            currentScore: 0.82,
            scoreDelta: 0.02,
            []);

        var markdown = ReportExporterTestsFixture.ToMarkdown(comparison);

        Assert.Contains("No Regression", markdown);
        Assert.DoesNotContain("REGRESSION DETECTED", markdown);
    }

    [Fact]
    public void ShouldGenerateMinimalMarkdown_WhenReportIsEmpty()
    {
        var report = ReportExporterTestsFixture.CreateEmptyReport("Empty");

        var markdown = ReportExporterTestsFixture.ToMarkdown(report);

        Assert.Contains("# Benchmark Report: Empty", markdown);
        Assert.Contains("**Overall Mean**: 0.0000", markdown);
    }
}
