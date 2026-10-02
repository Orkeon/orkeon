using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;

namespace Orkeon.Domain.Crew;

/// <summary>
/// Interface for crew process execution strategies. A strategy runs the crew's tasks in the order the
/// crew declares, sorted on their dependencies; no plan reaches it — the crew's plan
/// (<c>planning: true</c>) is a step-by-step plan per task, read by each task in its prompt, and decides
/// neither the order nor the agents (GAP-31).
/// </summary>
public interface IProcessStrategy
{
    /// <summary>
    /// Executes tasks sequentially.
    /// </summary>
    /// <param name="crew">The crew to execute.</param>
    /// <param name="inputVariables">Optional user-supplied input variables for template interpolation in task descriptions.</param>
    /// <param name="cancellationToken">Cancellation token. Implementations should propagate it to every internal await (agent execution, LLM HTTP calls, etc.).</param>
    Task<CrewOutput> ExecuteSequentialAsync(Crew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes tasks in a hierarchical manner with a manager.
    /// </summary>
    /// <param name="crew">The crew to execute.</param>
    /// <param name="managerAgentId">The manager agent identifier.</param>
    /// <param name="inputVariables">Optional user-supplied input variables for template interpolation in task descriptions.</param>
    /// <param name="cancellationToken">Cancellation token. Implementations should propagate it to every internal await (agent execution, LLM HTTP calls, etc.).</param>
    Task<CrewOutput> ExecuteHierarchicalAsync(Crew crew, AgentId managerAgentId, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes tasks in parallel.
    /// </summary>
    /// <param name="crew">The crew to execute.</param>
    /// <param name="inputVariables">Optional user-supplied input variables for template interpolation in task descriptions.</param>
    /// <param name="cancellationToken">Cancellation token. Implementations should propagate it to every internal await (agent execution, LLM HTTP calls, etc.).</param>
    Task<CrewOutput> ExecuteParallelAsync(Crew crew, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes tasks autonomously: agents self-organise, delegate recursively,
    /// and optionally spawn sub-agents, all under budget control.
    /// </summary>
    /// <param name="crew">The crew to execute.</param>
    /// <param name="budget">The execution budget that constrains autonomous behaviour.</param>
    /// <param name="inputVariables">Optional user-supplied input variables for template interpolation in task descriptions.</param>
    /// <param name="cancellationToken">Cancellation token. Implementations should propagate it to every internal await (agent execution, LLM HTTP calls, etc.).</param>
    Task<CrewOutput> ExecuteAutonomousAsync(Crew crew, AgentExecutionBudget budget, IReadOnlyDictionary<string, string>? inputVariables = null, CancellationToken cancellationToken = default);
}
