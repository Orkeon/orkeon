namespace Orkeon.Application.Evaluation;

/// <summary>
/// Runs an evaluation suite multiple times per case and computes statistics.
/// </summary>
public interface IBenchmarkRunner
{
    /// <summary>
    /// Executes the benchmark and produces a report.
    /// </summary>
    System.Threading.Tasks.Task<BenchmarkReport> RunAsync(BenchmarkConfig config, CancellationToken ct = default);
}

/// <summary>
/// Configuration for a benchmark run.
/// </summary>
public sealed record BenchmarkConfig(
    IEvaluationDataset Dataset,
    IEvaluationSuite Suite,
    int RunsPerCase = 3,
    string? BaselineReportPath = null);

/// <summary>
/// Full benchmark report with statistics.
/// </summary>
public sealed record BenchmarkReport(
    string DatasetName,
    DateTime GeneratedAt,
    int RunsPerCase,
    IReadOnlyList<BenchmarkCaseResult> Results,
    BenchmarkSummary Summary);

/// <summary>
/// Benchmark results for a single case across multiple runs.
/// </summary>
public sealed record BenchmarkCaseResult(
    int CaseIndex,
    IReadOnlyList<EvaluationCaseResult> RunResults,
    IReadOnlyDictionary<string, double> MeanScores,
    IReadOnlyDictionary<string, double> StdDevScores);

/// <summary>
/// Aggregate benchmark statistics.
/// </summary>
public sealed record BenchmarkSummary(
    double OverallMean,
    double OverallStdDev,
    IReadOnlyDictionary<string, double> MeanByEvaluator,
    IReadOnlyDictionary<string, double> VarianceByEvaluator);
