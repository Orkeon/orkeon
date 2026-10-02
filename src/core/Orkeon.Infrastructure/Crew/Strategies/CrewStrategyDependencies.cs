using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.SharedKernel.Events;
using IAgentRepository = Orkeon.Domain.Agent.IAgentRepository;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// The collaborators every crew strategy needs before it can run anything: where tasks and agents
/// are read from and saved, who executes an agent, the memory scope the run writes through, and
/// who delivers the domain events the tasks and agents raise as the run moves them (GAP-21).
/// <para>
/// They are grouped because they always travel together. Taken one by one they filled a
/// constructor before the strategy could name a single thing specific to its own mode; passed as
/// one unit, what stays in a strategy signature is exactly what makes that mode different from
/// the others.
/// </para>
/// </summary>
public sealed class CrewStrategyDependencies
{
    /// <summary>Builds the shared dependency set. Every collaborator but the dispatcher is required.</summary>
    /// <param name="taskRepository">Where the planned tasks are read from, and saved as the run moves them.</param>
    /// <param name="agentRepository">Where the crew's agents are read from, and saved as the run moves them.</param>
    /// <param name="executionService">Runs one task with one agent.</param>
    /// <param name="memoryScope">The memory scope the execution context writes through.</param>
    /// <param name="domainEvents">
    /// Delivers the events of the tasks and agents as the run goes — the container always provides
    /// one (<c>AddOrkeonInfrastructure</c>); without it the run still moves and saves them, and their
    /// events stay queued.
    /// </param>
    public CrewStrategyDependencies(
        ITaskRepository taskRepository,
        IAgentRepository agentRepository,
        IAgentExecutionService executionService,
        IMemoryScope memoryScope,
        IDomainEventDispatcher? domainEvents = null)
    {
        ArgumentNullException.ThrowIfNull(taskRepository);
        ArgumentNullException.ThrowIfNull(agentRepository);
        ArgumentNullException.ThrowIfNull(executionService);
        ArgumentNullException.ThrowIfNull(memoryScope);

        TaskRepository = taskRepository;
        AgentRepository = agentRepository;
        ExecutionService = executionService;
        MemoryScope = memoryScope;
        DomainEvents = domainEvents;
    }

    /// <summary>Where the planned tasks are read from.</summary>
    public ITaskRepository TaskRepository { get; }

    /// <summary>Where the crew's agents are read from.</summary>
    public IAgentRepository AgentRepository { get; }

    /// <summary>Runs one task with one agent.</summary>
    public IAgentExecutionService ExecutionService { get; }

    /// <summary>The memory scope the execution context writes through.</summary>
    public IMemoryScope MemoryScope { get; }

    /// <summary>Delivers the events of the tasks and agents as the run goes; null when nothing does.</summary>
    public IDomainEventDispatcher? DomainEvents { get; }

    /// <summary>The lifecycle of the tasks and agents of a strategy's runs, logging through <paramref name="logger"/>.</summary>
    internal TaskLifecycle LifecycleFor(Microsoft.Extensions.Logging.ILogger logger) =>
        new(TaskRepository, AgentRepository, DomainEvents, logger);
}
