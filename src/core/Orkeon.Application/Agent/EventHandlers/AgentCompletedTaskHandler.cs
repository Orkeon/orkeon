
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.SharedKernel.Events;
using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Agent.EventHandlers;

/// <summary>
/// Handles the AgentCompletedTaskEvent to perform side effects
/// such as logging and updating metrics.
/// </summary>
public sealed partial class AgentCompletedTaskHandler : IDomainEventHandler<AgentCompletedTaskEvent>
{
    private readonly ILogger<AgentCompletedTaskHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AgentCompletedTaskHandler"/>.
    /// </summary>
    public AgentCompletedTaskHandler(ILogger<AgentCompletedTaskHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task HandleAsync(AgentCompletedTaskEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        LogAgentCompletedTask(
            domainEvent.AgentId,
            domainEvent.TaskId,
            domainEvent.OccurredAt);

        return System.Threading.Tasks.Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent {AgentId} completed task {TaskId} at {CompletedAt}")]
    private partial void LogAgentCompletedTask(object agentId, object taskId, DateTime completedAt);
}
