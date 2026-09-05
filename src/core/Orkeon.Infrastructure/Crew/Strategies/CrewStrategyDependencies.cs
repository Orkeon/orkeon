using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using IAgentRepository = Orkeon.Domain.Agent.IAgentRepository;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// The four collaborators every crew strategy needs before it can run anything: where tasks and
/// agents are read from, who executes an agent, and the memory scope the run writes through.
/// <para>
/// They are grouped because they always travel together. Taken one by one they filled a
/// constructor before the strategy could name a single thing specific to its own mode; passed as
/// one unit, what stays in a strategy signature is exactly what makes that mode different from
/// the others.
/// </para>
/// </summary>
public sealed class CrewStrategyDependencies
{
    /// <summary>Builds the shared dependency set. Every collaborator is required.</summary>
    /// <param name="taskRepository">Where the planned tasks are read from.</param>
    /// <param name="agentRepository">Where the crew's agents are read from.</param>
    /// <param name="executionService">Runs one task with one agent.</param>
    /// <param name="memoryScope">The memory scope the execution context writes through.</param>
    public CrewStrategyDependencies(
        ITaskRepository taskRepository,
        IAgentRepository agentRepository,
        IAgentExecutionService executionService,
        IMemoryScope memoryScope)
    {
        ArgumentNullException.ThrowIfNull(taskRepository);
        ArgumentNullException.ThrowIfNull(agentRepository);
        ArgumentNullException.ThrowIfNull(executionService);
        ArgumentNullException.ThrowIfNull(memoryScope);

        TaskRepository = taskRepository;
        AgentRepository = agentRepository;
        ExecutionService = executionService;
        MemoryScope = memoryScope;
    }

    /// <summary>Where the planned tasks are read from.</summary>
    public ITaskRepository TaskRepository { get; }

    /// <summary>Where the crew's agents are read from.</summary>
    public IAgentRepository AgentRepository { get; }

    /// <summary>Runs one task with one agent.</summary>
    public IAgentExecutionService ExecutionService { get; }

    /// <summary>The memory scope the execution context writes through.</summary>
    public IMemoryScope MemoryScope { get; }
}
