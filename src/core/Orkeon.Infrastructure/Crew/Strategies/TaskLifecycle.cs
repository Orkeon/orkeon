using Microsoft.Extensions.Logging;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.Events;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using IAgentRepository = Orkeon.Domain.Agent.IAgentRepository;
using ITaskRepository = Orkeon.Domain.Task.ITaskRepository;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// Moves a crew's tasks and agents through their lifecycle as a run goes (GAP-21): applies one
/// transition of a <see cref="DomainTask"/> and of the agents it concerns, saves them in their
/// repositories — the run's scoped ones —, then hands their queued domain events to
/// <see cref="IDomainEventDispatcher"/>, so <c>TaskStartedEvent</c>, <c>AgentCompletedTaskEvent</c> and
/// their siblings reach the <c>IDomainEventHandler&lt;T&gt;</c> registrations while the run goes, before
/// the crew's own events, which the orchestrator dispatches when the kickoff ends.
/// <para>
/// <see cref="CrewRunOutcome"/> is its only caller: the six modes record each start, success, failure,
/// skip, task never reached and interruption there, and the outcome decides the transition.
/// </para>
/// <para>
/// Bookkeeping never changes a run: a transition the aggregate refuses is logged and skipped, a
/// handler that throws is logged and the events after it still go out — the same rule as the
/// crew's dispatch. Saving and dispatching ignore the run's token: a cancelled run still says what
/// became of its tasks. Without a dispatcher (a strategy built by hand) the transitions are applied
/// and saved, and the events stay queued on their aggregates.
/// </para>
/// </summary>
internal sealed partial class TaskLifecycle
{
    private readonly ITaskRepository _tasks;
    private readonly IAgentRepository _agents;
    private readonly IDomainEventDispatcher? _events;
    private readonly ILogger _logger;

    /// <summary>Builds the lifecycle of one strategy's runs.</summary>
    /// <param name="tasks">Where the run's tasks are saved.</param>
    /// <param name="agents">Where the run's agents are saved.</param>
    /// <param name="events">Delivers the events; null applies and saves without delivering.</param>
    /// <param name="logger">Receives a refused transition and a failing handler.</param>
    internal TaskLifecycle(ITaskRepository tasks, IAgentRepository agents, IDomainEventDispatcher? events, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(logger);
        _tasks = tasks;
        _agents = agents;
        _events = events;
        _logger = logger;
    }

    /// <summary>The task <paramref name="taskId"/> names, or null when the repository has none.</summary>
    internal System.Threading.Tasks.Task<DomainTask?> FindAsync(TaskId taskId) =>
        _tasks.GetByIdAsync(taskId, CancellationToken.None);

    /// <summary>
    /// Applies <paramref name="transition"/> to <paramref name="task"/> and <paramref name="agents"/>,
    /// saves them, then dispatches the events they queued — the task's first, then each agent's.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Lifecycle fault barrier: a transition the aggregate refuses is logged and the run goes on; bookkeeping never changes a run's result.")]
    internal async System.Threading.Tasks.Task ApplyAsync(
        DomainTask task, IReadOnlyCollection<DomainAgent> agents, Action transition)
    {
        try
        {
            transition();
        }
        catch (InvalidOperationException ex)
        {
            LogTransitionRefused(_logger, ex, task.Id, ex.Message);
        }

        await _tasks.UpdateAsync(task, CancellationToken.None).ConfigureAwait(false);
        foreach (var agent in agents)
            await _agents.UpdateAsync(agent, CancellationToken.None).ConfigureAwait(false);

        await DispatchAsync(task, task.Id).ConfigureAwait(false);
        foreach (var agent in agents)
            await DispatchAsync(agent, task.Id).ConfigureAwait(false);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Observer fault barrier: a failing domain-event handler must not alter the run, like the crew's dispatch.")]
    private async System.Threading.Tasks.Task DispatchAsync(IHasDomainEvents aggregate, TaskId taskId)
    {
        if (_events is null)
            return;

        DomainEvent[] pending;
        lock (aggregate)
        {
            pending = [.. aggregate.DomainEvents];
            aggregate.ClearDomainEvents();
        }

        foreach (var domainEvent in pending)
        {
            try
            {
                await _events.DispatchAsync(domainEvent, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogHandlerFailed(_logger, ex, domainEvent.GetType().Name, taskId);
            }
        }
    }

    [LoggerMessage(EventId = 9430, Level = LogLevel.Warning,
        Message = "The lifecycle of task {TaskId} could not move as the run did: {Reason}")]
    private static partial void LogTransitionRefused(ILogger logger, Exception ex, TaskId taskId, string reason);

    [LoggerMessage(EventId = 9431, Level = LogLevel.Warning,
        Message = "A handler of domain event {EventName} raised for task {TaskId} failed; the run goes on")]
    private static partial void LogHandlerFailed(ILogger logger, Exception ex, string eventName, TaskId taskId);
}
