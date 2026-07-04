using Orkeon.Application.Evaluation;

namespace Orkeon.Application.Interfaces.Training;

/// <summary>
/// Collects and generates feedback from training evaluation scores.
/// </summary>
public interface IFeedbackCollector
{
    /// <summary>
    /// Collects feedback for an agent based on task output and evaluation scores.
    /// </summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="taskOutput">The task output that was evaluated.</param>
    /// <param name="scores">The evaluation scores.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Feedback with suggestions for improvement.</returns>
    System.Threading.Tasks.Task<TrainingFeedback> CollectFeedbackAsync(
        string agentId,
        string taskOutput,
        IReadOnlyList<EvaluationScore> scores,
        CancellationToken ct = default);
}
