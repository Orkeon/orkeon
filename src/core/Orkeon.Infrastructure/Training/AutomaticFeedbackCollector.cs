using System.Globalization;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Evaluation;
using Orkeon.Application.Interfaces.Training;

namespace Orkeon.Infrastructure.Training;

/// <summary>
/// Automatically generates feedback from evaluation scores by analyzing
/// low-scoring evaluators and suggesting improvements.
/// </summary>
public sealed partial class AutomaticFeedbackCollector : IFeedbackCollector
{
    private readonly ILogger<AutomaticFeedbackCollector> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AutomaticFeedbackCollector"/>.
    /// </summary>
    public AutomaticFeedbackCollector(
        IEnumerable<IEvaluator> evaluators,
        ILogger<AutomaticFeedbackCollector> logger)
    {
        ArgumentNullException.ThrowIfNull(evaluators);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<TrainingFeedback> CollectFeedbackAsync(
        string agentId,
        string taskOutput,
        IReadOnlyList<EvaluationScore> scores,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(scores);

        var suggestions = new List<string>();

        foreach (var score in scores)
        {
            if (score.Score < 0.5)
            {
                suggestions.Add(string.Create(CultureInfo.InvariantCulture, $"Improve {score.EvaluatorName}: current score {score.Score:F2}"));
            }
        }

        var averageScore = scores.Count > 0
            ? scores.Average(s => s.Score)
            : 0.0;

        var summary = string.Create(CultureInfo.InvariantCulture, $"Agent {agentId} scored {averageScore:F2} average across {scores.Count} evaluators");

        LogGeneratedFeedbackForAgentSuggestion(agentId, summary, suggestions.Count);

        var feedback = new TrainingFeedback(
            agentId,
            summary,
            suggestions.AsReadOnly(),
            DateTime.UtcNow);

        return Task.FromResult(feedback);
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Generated feedback for agent {AgentId}: {Summary}, {SuggestionCount} suggestion(s)")]
    private partial void LogGeneratedFeedbackForAgentSuggestion(object agentId, object summary, int suggestionCount);

}
