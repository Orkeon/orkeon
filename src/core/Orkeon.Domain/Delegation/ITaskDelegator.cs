using Orkeon.Domain.Common;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Domain.Delegation;

/// <summary>
/// Manages delegation of tasks between agents.
/// </summary>
public interface ITaskDelegator
{
    /// <summary>
    /// Delegates a task to another agent.
    /// </summary>
    System.Threading.Tasks.Task<bool> DelegateTaskAsync(
        AgentId fromAgentId,
        AgentId toAgentId,
        TaskId taskId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the best agent to delegate a task to.
    /// </summary>
    System.Threading.Tasks.Task<DomainAgent?> FindBestAgentForTaskAsync(
        Task.CrewTask task,
        IEnumerable<DomainAgent> availableAgents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an agent can delegate to another agent.
    /// </summary>
    System.Threading.Tasks.Task<bool> CanDelegateAsync(
        AgentId fromAgentId,
        AgentId toAgentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records delegation history for learning purposes.
    /// </summary>
    System.Threading.Tasks.Task RecordDelegationAsync(
        AgentId fromAgentId,
        AgentId toAgentId,
        TaskId taskId,
        bool success,
        string? feedback = null,
        CancellationToken cancellationToken = default);
}
