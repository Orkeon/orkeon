using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Application.Execution;
using Orkeon.Application.Context;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Interfaces;

/// <summary>
/// The manager of a hierarchical crew — and the one that hands an autonomous crew's tasks out: it
/// assigns each task to an agent and reviews what comes back, on the LLM the crew gives it
/// (<see cref="ManagerLlm"/>, resolved once per run by the strategy — GAP-19).
/// </summary>
public interface IManagerAgent
{
    /// <summary>Assigns a task to the most appropriate agent from the available list.</summary>
    /// <param name="task">The task to assign.</param>
    /// <param name="availableAgents">The agents the task may go to — the crew's workers.</param>
    /// <param name="context">The run's execution context.</param>
    /// <param name="llm">The LLM the crew gives its manager.</param>
    /// <returns>The assignment, with the manager's reason.</returns>
    System.Threading.Tasks.Task<TaskAssignment> AssignTaskAsync(
        CrewTask task,
        IReadOnlyList<DomainAgent> availableAgents,
        SimpleExecutionContext context,
        ManagerLlm llm
    );

    /// <summary>Reviews a task's output against the task.</summary>
    /// <param name="output">What the assigned agent produced.</param>
    /// <param name="originalTask">The task it answers.</param>
    /// <param name="llm">The LLM the crew gives its manager.</param>
    /// <returns>True when the manager accepts the output.</returns>
    System.Threading.Tasks.Task<bool> ReviewOutputAsync(
        TaskOutput output,
        CrewTask originalTask,
        ManagerLlm llm
    );
}
