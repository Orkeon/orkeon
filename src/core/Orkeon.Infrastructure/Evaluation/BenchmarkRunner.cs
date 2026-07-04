using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Evaluation;

/// <summary>
/// Runs an evaluation suite multiple times per case and computes mean/stddev statistics.
/// LLM-based evaluators are skipped when no IChatClient is available.
/// </summary>
public sealed class BenchmarkRunner : IBenchmarkRunner
{
    /// <inheritdoc />
    public Task<BenchmarkReport> RunAsync(BenchmarkConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        return RunCoreAsync();

        async Task<BenchmarkReport> RunCoreAsync()
        {
        var inputs = await config.Dataset.LoadAsync(ct).ConfigureAwait(false);
        var runsPerCase = Math.Max(1, config.RunsPerCase);

        var caseResults = new List<BenchmarkCaseResult>();

        for (var caseIdx = 0; caseIdx < inputs.Count; caseIdx++)
        {
            var singleInput = new List<EvaluationInput> { inputs[caseIdx] };
            var runResults = new List<EvaluationCaseResult>();

            for (var run = 0; run < runsPerCase; run++)
            {
                ct.ThrowIfCancellationRequested();
                var report = await config.Suite.RunAsync(singleInput, ct).ConfigureAwait(false);
                if (report.Results.Count > 0)
                    runResults.Add(report.Results[0]);
            }

            var (meanScores, stdDevScores) = ComputeStats(runResults, config.Suite.Evaluators);
            caseResults.Add(new BenchmarkCaseResult(
                CaseIndex: caseIdx,
                RunResults: runResults.AsReadOnly(),
                MeanScores: meanScores,
                StdDevScores: stdDevScores));
        }

        var summary = ComputeSummary(caseResults, config.Suite.Evaluators);

        return new BenchmarkReport(
            DatasetName: config.Dataset.Name,
            GeneratedAt: DateTime.UtcNow,
            RunsPerCase: runsPerCase,
            Results: caseResults.AsReadOnly(),
            Summary: summary);
        }
    }

    private static (Dictionary<string, double> Mean, Dictionary<string, double> StdDev)
        ComputeStats(List<EvaluationCaseResult> runResults, IReadOnlyList<IEvaluator> evaluators)
    {
        var statsPerEvaluator = evaluators
            .Select(evaluator => new
            {
                evaluator.Name,
                Scores = runResults
                    .SelectMany(r => r.Scores)
                    .Where(s => s.EvaluatorName == evaluator.Name)
                    .Select(s => s.Score)
                    .ToList()
            })
            .Where(x => x.Scores.Count > 0)
            .Select(x =>
            {
                var avg = x.Scores.Average();
                var variance = x.Scores.Count > 1
                    ? x.Scores.Select(s => (s - avg) * (s - avg)).Sum() / (x.Scores.Count - 1)
                    : 0.0;
                return new { x.Name, Mean = Math.Round(avg, 4), StdDev = Math.Round(Math.Sqrt(variance), 4) };
            })
            .ToList();

        var mean = statsPerEvaluator.ToDictionary(x => x.Name, x => x.Mean);
        var stdDev = statsPerEvaluator.ToDictionary(x => x.Name, x => x.StdDev);

        return (mean, stdDev);
    }

    private static BenchmarkSummary ComputeSummary(
        List<BenchmarkCaseResult> cases, IReadOnlyList<IEvaluator> evaluators)
    {
        if (cases.Count == 0)
        {
            return new BenchmarkSummary(0, 0,
                new Dictionary<string, double>(),
                new Dictionary<string, double>());
        }

        // Overall mean: average of all per-case means across all evaluators
        var allMeans = cases
            .SelectMany(c => c.MeanScores.Values)
            .ToList();

        var overallMean = allMeans.Count > 0 ? allMeans.Average() : 0.0;

        var overallVariance = allMeans.Count > 1
            ? allMeans.Select(m => (m - overallMean) * (m - overallMean)).Sum() / (allMeans.Count - 1)
            : 0.0;
        var overallStdDev = Math.Sqrt(overallVariance);

        // Per-evaluator means and variance across cases
        var evalStats = evaluators
            .Select(evaluator => new
            {
                evaluator.Name,
                CaseMeans = cases
                    .Select(c => c.MeanScores.TryGetValue(evaluator.Name, out var score) ? (double?)score : null)
                    .Where(s => s.HasValue)
                    .Select(s => s!.Value)
                    .ToList()
            })
            .Where(x => x.CaseMeans.Count > 0)
            .Select(x =>
            {
                var avg = x.CaseMeans.Average();
                var variance = x.CaseMeans.Count > 1
                    ? x.CaseMeans.Select(m => (m - avg) * (m - avg)).Sum() / (x.CaseMeans.Count - 1)
                    : 0.0;
                return new { x.Name, Mean = Math.Round(avg, 4), Variance = Math.Round(variance, 6) };
            })
            .ToList();

        var meanByEvaluator = evalStats.ToDictionary(x => x.Name, x => x.Mean);
        var varianceByEvaluator = evalStats.ToDictionary(x => x.Name, x => x.Variance);

        return new BenchmarkSummary(
            Math.Round(overallMean, 4),
            Math.Round(overallStdDev, 4),
            meanByEvaluator,
            varianceByEvaluator);
    }
}
