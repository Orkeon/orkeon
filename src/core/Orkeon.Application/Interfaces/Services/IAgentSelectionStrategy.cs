using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// Strategy for selecting the best agent for a given task.
/// </summary>
public interface IAgentSelectionStrategy
{
    /// <summary>
    /// Selects the best agent for the given task from the available agents.
    /// </summary>
    /// <param name="task">The task to be assigned.</param>
    /// <param name="availableAgents">The pool of available agents.</param>
    /// <param name="currentAgent">The agent currently considering delegation (excluded from selection).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The best matching agent, or null if no suitable agent is found.</returns>
    System.Threading.Tasks.Task<DomainAgent?> SelectBestAgentAsync(
        ICrewTask task,
        IEnumerable<DomainAgent> availableAgents,
        DomainAgent? currentAgent = null,
        CancellationToken cancellationToken = default);
}
