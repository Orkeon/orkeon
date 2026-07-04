using Orkeon.Application.Evaluation;
using Orkeon.Infrastructure.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation;

public class CompareReportsTestsFixture
{
    public static BenchmarkReport CreateReport(
        double overallMean, Dictionary<string, double> meanByEvaluator)
    {
        var variance = meanByEvaluator.ToDictionary(kv => kv.Key, _ => 0.001);
        return new BenchmarkReport(
            DatasetName: "TestData",
            GeneratedAt: DateTime.UtcNow,
            RunsPerCase: 3,
            Results: Array.Empty<BenchmarkCaseResult>(),
            Summary: new BenchmarkSummary(overallMean, 0.01, meanByEvaluator, variance));
    }

    public static ComparisonResult Compare(BenchmarkReport baseline, BenchmarkReport current, double regressionThreshold = 0.0)
        => CompareReports.Compare(baseline, current, regressionThreshold: regressionThreshold);
}
