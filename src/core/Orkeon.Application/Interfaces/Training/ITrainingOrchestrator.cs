namespace Orkeon.Application.Interfaces.Training;

/// <summary>
/// Orchestrates agent training by running evaluation suites across training plans.
/// </summary>
public interface ITrainingOrchestrator
{
    /// <summary>
    /// Trains an agent using the specified plan and options.
    /// </summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="plan">The training plan to execute.</param>
    /// <param name="options">Optional training options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The training result for the final iteration.</returns>
    System.Threading.Tasks.Task<TrainingResult> TrainAgentAsync(
        string agentId,
        TrainingPlan plan,
        TrainingOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves the training history for an agent.
    /// </summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All training results for the agent.</returns>
    System.Threading.Tasks.Task<IReadOnlyList<TrainingResult>> GetTrainingHistoryAsync(
        string agentId,
        CancellationToken ct = default);
}
