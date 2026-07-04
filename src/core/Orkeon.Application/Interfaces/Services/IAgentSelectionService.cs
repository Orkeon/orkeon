using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Task;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// Service for selecting the best agent for a task using semantic similarity
/// </summary>
public interface IAgentSelectionService
{
    /// <summary>
    /// Selects the best agent for a given task
    /// </summary>
    /// <param name="agents">Available agents to choose from</param>
    /// <param name="task">Task to be executed</param>
    /// <param name="embedder">Embedding service to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>DomainAgent selection result with the best match</returns>
    System.Threading.Tasks.Task<AgentSelectionResult> SelectBestAgentAsync(
        IReadOnlyList<DomainAgent> agents,
        CrewTask task,
        IEmbeddingService embedder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets agent capabilities with embeddings
    /// </summary>
    /// <param name="agent">DomainAgent to analyze</param>
    /// <param name="embedder">Embedding service to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>DomainAgent capability with embedding</returns>
    System.Threading.Tasks.Task<AgentCapability> GetAgentCapabilityAsync(
        DomainAgent agent,
        IEmbeddingService embedder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets task requirements with embeddings
    /// </summary>
    /// <param name="task">Task to analyze</param>
    /// <param name="embedder">Embedding service to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task requirement with embedding</returns>
    System.Threading.Tasks.Task<TaskRequirement> GetTaskRequirementAsync(
        CrewTask task,
        IEmbeddingService embedder,
        CancellationToken cancellationToken = default);
}
