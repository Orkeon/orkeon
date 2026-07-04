using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Evaluation;

/// <summary>
/// Default implementation of <see cref="IEvaluationSuite"/> that runs all evaluators
/// on each input and aggregates results.
/// </summary>
public sealed class EvaluationSuite : IEvaluationSuite
{
    private readonly List<IEvaluator> _evaluators;

    /// <inheritdoc />
    public string Name { get; }
    /// <inheritdoc />
    public IReadOnlyList<IEvaluator> Evaluators => _evaluators.AsReadOnly();

    /// <summary>Initializes a new instance of <see cref="EvaluationSuite"/>.</summary>
    /// <param name="name">The suite name.</param>
    /// <param name="evaluators">The initial evaluators.</param>
    public EvaluationSuite(string name, IEnumerable<IEvaluator>? evaluators = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        _evaluators = evaluators?.ToList() ?? [];
    }

    /// <inheritdoc />
    public IEvaluationSuite AddEvaluator(IEvaluator evaluator)
    {
        var newList = new List<IEvaluator>(_evaluators) { evaluator };
        return new EvaluationSuite(Name, newList);
    }

    /// <inheritdoc />
    public Task<EvaluationReport> RunAsync(
        IReadOnlyList<EvaluationInput> inputs, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return RunCoreAsync();

        async Task<EvaluationReport> RunCoreAsync()
        {
            var caseResults = new List<EvaluationCaseResult>();

            for (var i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                var scores = new List<EvaluationScore>();

                foreach (var evaluator in _evaluators)
                {
                    ct.ThrowIfCancellationRequested();
                    var score = await evaluator.EvaluateAsync(input, ct).ConfigureAwait(false);
                    scores.Add(score);
                }

                var avgScore = scores.Count > 0 ? scores.Average(s => s.Score) : 0.0;
                caseResults.Add(new EvaluationCaseResult(
                    CaseIndex: i,
                    CaseDescription: input.TaskDescription,
                    Scores: scores.AsReadOnly(),
                    AverageScore: Math.Round(avgScore, 4)));
            }

            var summary = ComputeSummary(caseResults);
            return new EvaluationReport(Name, DateTime.UtcNow, caseResults.AsReadOnly(), summary);
        }
    }

    private EvaluationSummary ComputeSummary(List<EvaluationCaseResult> results)
    {
        var totalCases = results.Count;
        if (totalCases == 0)
        {
            return new EvaluationSummary(
                0, 0.0,
                new Dictionary<string, double>(),
                new Dictionary<string, double>());
        }

        var overallScore = results.Average(r => r.AverageScore);

        // Per-evaluator averages and variance
        var evalStats = _evaluators
            .Select(evaluator => new
            {
                evaluator.Name,
                Scores = results
                    .SelectMany(r => r.Scores)
                    .Where(s => s.EvaluatorName == evaluator.Name)
                    .Select(s => s.Score)
                    .ToList()
            })
            .Where(x => x.Scores.Count > 0)
            .Select(x =>
            {
                var mean = x.Scores.Average();
                var variance = x.Scores.Count > 1
                    ? x.Scores.Select(s => (s - mean) * (s - mean)).Average()
                    : 0.0;
                return new { x.Name, Mean = Math.Round(mean, 4), Variance = Math.Round(variance, 6) };
            })
            .ToList();

        var scoresByEvaluator = evalStats.ToDictionary(x => x.Name, x => x.Mean);
        var varianceByEvaluator = evalStats.ToDictionary(x => x.Name, x => x.Variance);

        return new EvaluationSummary(
            totalCases,
            Math.Round(overallScore, 4),
            scoresByEvaluator,
            varianceByEvaluator);
    }
}
