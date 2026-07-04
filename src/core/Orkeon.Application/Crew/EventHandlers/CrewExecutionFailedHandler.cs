
using Orkeon.Domain.Crew.Events;
using Orkeon.Domain.SharedKernel.Events;
using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Crew.EventHandlers;

/// <summary>
/// Handles the CrewExecutionFailedEvent to perform side effects
/// such as logging warnings for failed crew executions.
/// </summary>
public sealed partial class CrewExecutionFailedHandler : IDomainEventHandler<CrewExecutionFailedEvent>
{
    private readonly ILogger<CrewExecutionFailedHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CrewExecutionFailedHandler"/>.
    /// </summary>
    public CrewExecutionFailedHandler(ILogger<CrewExecutionFailedHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task HandleAsync(CrewExecutionFailedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        LogCrewExecutionFailed(
            domainEvent.CrewId.ToString(),
            domainEvent.Reason,
            domainEvent.OccurredAt);

        return System.Threading.Tasks.Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Crew {CrewId} execution failed: {Reason} at {FailedAt}")]
    private partial void LogCrewExecutionFailed(string crewId, string reason, DateTime failedAt);
}
