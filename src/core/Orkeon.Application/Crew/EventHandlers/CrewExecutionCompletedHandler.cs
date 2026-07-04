
using Orkeon.Domain.Crew.Events;
using Orkeon.Domain.SharedKernel.Events;
using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Crew.EventHandlers;

/// <summary>
/// Handles the CrewExecutionCompletedEvent to perform side effects
/// such as logging successful crew execution completions.
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
            domainEvent.FailedTasks,
            domainEvent.Duration,
            domainEvent.OccurredAt);

        return System.Threading.Tasks.Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Crew {CrewId} execution completed: {CompletedTasks} succeeded, {FailedTasks} failed, duration {Duration}, at {CompletedAt}")]
    private partial void LogCrewExecutionCompleted(object crewId, int completedTasks, int failedTasks, TimeSpan duration, DateTime completedAt);
}
