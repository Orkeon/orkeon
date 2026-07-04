
using Orkeon.Domain.Agent.Events;
using Orkeon.Domain.SharedKernel.Events;
using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Agent.EventHandlers;

/// <summary>
/// Handles the AgentFailedTaskEvent to perform side effects
/// such as logging warnings for failed task executions.
/// </summary>
public sealed partial class AgentFailedTaskHandler : IDomainEventHandler<AgentFailedTaskEvent>
{
    private readonly ILogger<AgentFailedTaskHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AgentFailedTaskHandler"/>.
    /// </summary>
    public AgentFailedTaskHandler(ILogger<AgentFailedTaskHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task HandleAsync(AgentFailedTaskEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        LogAgentFailedTask(
            domainEvent.AgentId.ToString(),
            domainEvent.TaskId.ToString(),
            domainEvent.Reason);

        return System.Threading.Tasks.Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent {AgentId} failed task {TaskId}: {Reason}")]
    private partial void LogAgentFailedTask(string agentId, string taskId, string reason);
}
