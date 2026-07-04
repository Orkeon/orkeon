using Microsoft.Extensions.Logging;
using Orkeon.Domain.Common;
using Orkeon.Domain.Delegation;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Stubs;

/// <summary>
/// No-op implementation of <see cref="ITaskDelegator"/> that always denies delegation.
/// Logs a warning on first use to indicate no delegation logic is configured.
/// </summary>
public sealed partial class NullTaskDelegator : ITaskDelegator
{
    private readonly ILogger<NullTaskDelegator> _logger;
    private int _warnedOnce;

    /// <summary>Initializes a new instance of <see cref="NullTaskDelegator"/>.</summary>
    public NullTaskDelegator(ILogger<NullTaskDelegator> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    private void WarnOnce()
    {
        if (Interlocked.Exchange(ref _warnedOnce, 1) == 0)
            LogNullDelegatorFallback();
    }

    /// <inheritdoc />
    public Task<bool> DelegateTaskAsync(
        AgentId fromAgentId, AgentId toAgentId, TaskId taskId, string reason,
        CancellationToken cancellationToken = default)
    {
        WarnOnce();
        if (_logger.IsEnabled(LogLevel.Debug))
            LogDelegationDenied(fromAgentId, toAgentId, taskId);
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<DomainAgent?> FindBestAgentForTaskAsync(
        Domain.Task.CrewTask task, IEnumerable<DomainAgent> availableAgents,
        CancellationToken cancellationToken = default)
    {
        WarnOnce();
        // Return the first available agent as a simple fallback
        return Task.FromResult(availableAgents.FirstOrDefault());
    }

    /// <inheritdoc />
    public Task<bool> CanDelegateAsync(
        AgentId fromAgentId, AgentId toAgentId,
        CancellationToken cancellationToken = default)
    {
        WarnOnce();
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task RecordDelegationAsync(
        AgentId fromAgentId, AgentId toAgentId, TaskId taskId, bool success, string? feedback = null,
        CancellationToken cancellationToken = default)
    {
        WarnOnce();
        return Task.CompletedTask;
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Using NullTaskDelegator — task delegation is disabled. Register a real ITaskDelegator to enable agent delegation.")]
    private partial void LogNullDelegatorFallback();

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Delegation request denied (stub): from={From}, to={To}, task={Task}")]
    private partial void LogDelegationDenied(AgentId from, AgentId to, TaskId task);
}
