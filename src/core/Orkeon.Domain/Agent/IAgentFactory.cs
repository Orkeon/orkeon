namespace Orkeon.Domain.Agent;

/// <summary>
/// Factory interface for creating agents at runtime.
/// Abstracts agent instantiation to support dynamic creation during crew execution.
/// </summary>
public interface IAgentFactory
{
    /// <summary>
    /// Creates a new agent based on the provided spawn request.
    /// </summary>
    /// <param name="request">The spawn request describing the agent to create.</param>
    /// <returns>A newly created agent instance.</returns>
    Agent CreateAgent(AgentSpawnRequest request);

    /// <summary>
    /// Asynchronously creates a new agent based on the provided spawn request.
    /// Allows for async initialization if needed.
    /// </summary>
    /// <param name="request">The spawn request describing the agent to create.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A task that returns the newly created agent instance.</returns>
    Task<Agent> CreateAgentAsync(AgentSpawnRequest request, CancellationToken cancellationToken = default);
}
