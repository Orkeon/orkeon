namespace Orkeon.Application.Interfaces;

/// <summary>
/// Strategy interface for executing a consensual process where agents
/// independently work on tasks and reach consensus through voting.
/// </summary>
public interface IConsensualProcessStrategy
{
    /// <summary>
    /// Executes crew tasks using a consensual process.
    /// Each task is executed independently by all agents, then votes are tallied
    /// to reach consensus on the best output.
    /// </summary>
    /// <param name="crew">The crew to execute.</param>
    /// <param name="plan">The execution plan defining task order.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The crew output after consensus is reached.</returns>
    System.Threading.Tasks.Task<Domain.Crew.CrewOutput> ExecuteConsensualAsync(
        Domain.Crew.Crew crew,
        Domain.Crew.ExecutionPlan plan,
        CancellationToken ct = default);
}
