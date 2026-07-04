using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Application.Execution;
using Orkeon.Application.Context;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Interfaces;

/// <summary>
/// IManagerAgent type.
/// </summary>
public interface IManagerAgent
{
    /// <summary>Assigns a task to the most appropriate agent from the available list.</summary>
    System.Threading.Tasks.Task<TaskAssignment> AssignTaskAsync(
        CrewTask task,
        IReadOnlyList<DomainAgent> availableAgents,
        SimpleExecutionContext context
    );

    /// <summary>Review Output Async(Task Output, Task).</summary>
    System.Threading.Tasks.Task<bool> ReviewOutputAsync(
        TaskOutput output,
        CrewTask originalTask
    );
}
