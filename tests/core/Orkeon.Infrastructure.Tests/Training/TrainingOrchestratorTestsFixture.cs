using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Evaluation;
using Orkeon.Application.Interfaces.Training;
using Orkeon.Infrastructure.Training;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Infrastructure.Tests.Training;

public class TrainingOrchestratorTestsFixture
{
    public static TrainingOrchestrator CreateOrchestrator(double evaluatorScore)
    {
        var evaluator = new StubEvaluator("TestEvaluator", evaluatorScore);
        var suite = new Infrastructure.Evaluation.EvaluationSuite("TestSuite", [evaluator]);
        var feedbackCollector = new StubFeedbackCollector();
        var logger = NullLogger<TrainingOrchestrator>.Instance;

        return new TrainingOrchestrator(suite, feedbackCollector, logger);
    }

    public static TrainingPlan CreateSingleTaskPlan(string name, string description, string? expectedOutput = null)
    {
        return new TrainingPlan(
            name,
            [new TrainingTask(TaskId1, description, expectedOutput)]);
    }

    private sealed class StubEvaluator : IEvaluator
    {
        private readonly double _score;

        public string Name { get; }
        public string Description => "Stub evaluator for testing";
        public bool RequiresLlm => false;

        public StubEvaluator(string name, double score)
        {
            Name = name;
            _score = score;
        }

        public Task<EvaluationScore> EvaluateAsync(EvaluationInput input, CancellationToken ct = default)
        {
            return Task.FromResult(new EvaluationScore(Name, _score, "Stub evaluation"));
        }
    }

    private sealed class StubFeedbackCollector : IFeedbackCollector
    {
        public Task<TrainingFeedback> CollectFeedbackAsync(
            string agentId,
            string taskOutput,
            IReadOnlyList<EvaluationScore> scores,
            CancellationToken ct = default)
        {
            var avg = scores.Count > 0 ? scores.Average(s => s.Score) : 0.0;
            var suggestions = scores
                .Where(s => s.Score < 0.5)
                .Select(s => $"Improve {s.EvaluatorName}")
                .ToList();

            return Task.FromResult(new TrainingFeedback(
                agentId,
                $"Stub feedback: avg={avg:F2}",
                suggestions.AsReadOnly(),
                DateTime.UtcNow));
        }
    }
}
