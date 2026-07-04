using Orkeon.Domain.AgentCommunication;

namespace Orkeon.Application.Interfaces.AgentCommunication;

/// <summary>
/// Discovers remote A2A agents by fetching their agent cards from well-known endpoints.
/// </summary>
public interface IA2AAgentDiscovery
{
    /// <summary>
    /// Discovers a single agent by fetching its agent card from
    /// <c>{agentUrl}/.well-known/agent.json</c>.
    /// </summary>
    /// <param name="agentUrl">The base URL of the remote agent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The agent card, or <c>null</c> if discovery failed.</returns>
    System.Threading.Tasks.Task<AgentCard?> DiscoverAsync(Uri agentUrl, CancellationToken ct = default);

    /// <summary>
    /// Discovers multiple agents in parallel.
    /// Agents that fail discovery are silently skipped.
    /// </summary>
    /// <param name="agentUrls">The base URLs of the remote agents.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of successfully discovered agent cards.</returns>
    System.Threading.Tasks.Task<IReadOnlyList<AgentCard>> DiscoverMultipleAsync(
        IEnumerable<string> agentUrls, CancellationToken ct = default);
}
