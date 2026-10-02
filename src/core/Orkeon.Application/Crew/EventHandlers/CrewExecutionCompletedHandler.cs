
using Orkeon.Domain.Crew.Events;
using Orkeon.Domain.SharedKernel.Events;
using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Crew.EventHandlers;

/// <summary>
/// Handles the CrewExecutionCompletedEvent to perform side effects
/// such as logging successful crew execution completions: every task the run ran succeeded.
/// </summary>
public sealed partial class CrewExecutionCompletedHandler : IDomainEventHandler<CrewExecutionCompletedEvent>
{
    private readonly ILogger<CrewExecutionCompletedHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CrewExecutionCompletedHandler"/>.
    /// </summary>
    public CrewExecutionCompletedHandler(ILogger<CrewExecutionCompletedHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task HandleAsync(CrewExecutionCompletedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        LogCrewExecutionCompleted(
            domainEvent.CrewId,
            domainEvent.CompletedTasks,
            domainEvent.Duration,
            domainEvent.OccurredAt);

        return System.Threading.Tasks.Task.CompletedTask;
    }

    // A completed run is a run whose every task succeeded (GAP-32): a failed task fails the run,
    // which CrewExecutionFailedHandler reports with its reason. There is no "N failed" to log.
    [LoggerMessage(Level = LogLevel.Information, Message = "Crew {CrewId} execution completed: {CompletedTasks} tasks, duration {Duration}, at {CompletedAt}")]
    private partial void LogCrewExecutionCompleted(object crewId, int completedTasks, TimeSpan duration, DateTime completedAt);
}
