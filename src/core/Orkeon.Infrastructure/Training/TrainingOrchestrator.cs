using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Evaluation;
using Orkeon.Application.Interfaces.Training;

namespace Orkeon.Infrastructure.Training;

/// <summary>
/// Default implementation of <see cref="ITrainingOrchestrator"/> that uses an evaluation suite
/// to train agents by running tasks through evaluation and collecting feedback.
/// </summary>
public sealed partial class TrainingOrchestrator : ITrainingOrchestrator
{
    private readonly IEvaluationSuite _evaluationSuite;
    private readonly IFeedbackCollector _feedbackCollector;
    private readonly ILogger<TrainingOrchestrator> _logger;
    private readonly ConcurrentDictionary<string, List<TrainingResult>> _history = new();

    /// <summary>
    /// Initializes a new instance of <see cref="TrainingOrchestrator"/>.
    /// </summary>
    public TrainingOrchestrator(
        IEvaluationSuite evaluationSuite,
        IFeedbackCollector feedbackCollector,
        ILogger<TrainingOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(evaluationSuite);
        _evaluationSuite = evaluationSuite;
        ArgumentNullException.ThrowIfNull(feedbackCollector);
        _feedbackCollector = feedbackCollector;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<TrainingResult> TrainAgentAsync(
        string agentId,
        TrainingPlan plan,
        TrainingOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(plan);

        return TrainAgentAsyncCore(agentId, plan, options, ct);
    }

    private async Task<TrainingResult> TrainAgentAsyncCore(
        string agentId,
        TrainingPlan plan,
        TrainingOptions? options,
        CancellationToken ct)
    {
        var opts = options ?? new TrainingOptions();
        TrainingResult? lastResult = null;

        LogStartingTrainingForAgentWith(agentId, plan.Name, plan.Iterations);

        for (var iteration = 1; iteration <= plan.Iterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();

            var allScores = new List<EvaluationScore>();

            foreach (var task in plan.Tasks)
            {
                ct.ThrowIfCancellationRequested();

                // Simulate agent output using task description (no real agent execution)
                var simulatedOutput = task.Description;

                var inputs = new List<EvaluationInput>
                {
                    new(
                        Output: simulatedOutput,
                        ExpectedOutput: task.ExpectedOutput,
                        TaskDescription: task.Description)
                };

                var report = await _evaluationSuite.RunAsync(inputs, ct).ConfigureAwait(false);

                foreach (var caseResult in report.Results)
                {
                    allScores.AddRange(caseResult.Scores);
                }
            }

            var averageScore = allScores.Count > 0
                ? allScores.Average(s => s.Score)
                : 0.0;

            TrainingFeedback? feedback = null;
            if (opts.CollectFeedback && allScores.Count > 0)
            {
                feedback = await _feedbackCollector.CollectFeedbackAsync(
                    agentId,
                    string.Join("; ", plan.Tasks.Select(t => t.Description)),
                    allScores,
                    ct).ConfigureAwait(false);
            }

            lastResult = new TrainingResult
            {
                AgentId = agentId,
                PlanName = plan.Name,
                Iteration = iteration,
                AverageScore = Math.Round(averageScore, 4),
                Passed = averageScore >= opts.PassingThreshold,
                Scores = allScores.AsReadOnly(),
                Feedback = feedback,
                CompletedAt = DateTime.UtcNow
            };

            // Store in history
            var agentHistory = _history.GetOrAdd(agentId, _ => []);
            lock (agentHistory)
            {
                agentHistory.Add(lastResult);
            }

            LogAgentIterationAverageScorePassed(agentId, iteration, lastResult.AverageScore, lastResult.Passed);
        }

        return lastResult!;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TrainingResult>> GetTrainingHistoryAsync(
        string agentId,
        CancellationToken ct = default)
    {
        if (_history.TryGetValue(agentId, out var history))
        {
            List<TrainingResult> snapshot;
            lock (history)
            {
                snapshot = [.. history];
            }
            return Task.FromResult<IReadOnlyList<TrainingResult>>(snapshot.AsReadOnly());
        }

        return Task.FromResult<IReadOnlyList<TrainingResult>>(
            Array.Empty<TrainingResult>());
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Starting training for agent {AgentId} with plan '{PlanName}', {Iterations} iteration(s)")]
    private partial void LogStartingTrainingForAgentWith(string agentId, string planName, int iterations);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Agent {AgentId} iteration {Iteration}: average score {Score:F4}, passed={Passed}")]
    private partial void LogAgentIterationAverageScorePassed(string agentId, int iteration, double score, bool passed);

}
