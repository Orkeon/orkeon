
namespace Orkeon.Application.Interfaces.Training;

/// <summary>
/// Tracks agent performance over time to identify trends and improvement.
/// </summary>
public interface IAgentPerformanceTracker
{
    /// <summary>
    /// Records a performance data point for an agent.
    /// </summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="taskId">The task identifier.</param>
    /// <param name="score">The performance score.</param>
    /// <param name="metadata">Optional metadata about the performance.</param>
    /// <param name="ct">Cancellation token.</param>
    System.Threading.Tasks.Task RecordPerformanceAsync(
        string agentId,
        string taskId,
        double score,
        IDictionary<string, object>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the performance trend for an agent over the last N data points.
    /// </summary>
    /// <param name="agentId">The agent identifier.</param>
    /// <param name="lastN">Number of recent data points to include.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The performance trend including average and improvement.</returns>
    System.Threading.Tasks.Task<PerformanceTrend> GetPerformanceTrendAsync(
        string agentId,
        int lastN = 10,
        CancellationToken ct = default);
}
