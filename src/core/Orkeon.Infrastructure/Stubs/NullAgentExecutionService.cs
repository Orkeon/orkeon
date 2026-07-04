using Microsoft.Extensions.Logging;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Services;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Stubs;

/// <summary>
/// No-op implementation of <see cref="IAgentExecutionService"/> that returns empty results.
/// Logs a warning on first use. Consumer should override with a real implementation.
/// </summary>
public sealed partial class NullAgentExecutionService : IAgentExecutionService
{
    private readonly ILogger<NullAgentExecutionService> _logger;
    private int _warnedOnce;

    /// <summary>Initializes a new instance of <see cref="NullAgentExecutionService"/>.</summary>
    public NullAgentExecutionService(ILogger<NullAgentExecutionService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    private void WarnOnce()
    {
        if (Interlocked.Exchange(ref _warnedOnce, 1) == 0)
            LogNullServiceFallback();
    }

    /// <inheritdoc />
    public Task<TaskResult> ExecuteTaskAsync(
        DomainAgent agent,
        Domain.Task.ICrewTask task,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        WarnOnce();
        if (_logger.IsEnabled(LogLevel.Trace))
            LogExecuteTaskCalled(agent.Id);
        return Task.FromResult(new TaskResult(
            Success: false,
            Output: "(no execution service configured)",
            StructuredOutput: null,
            ToolsUsed: [],
            ExecutionTime: TimeSpan.Zero,
            Error: "No IAgentExecutionService registered"));
    }

    /// <inheritdoc />
    public Task<TaskResult<TOutput>> ExecuteTaskAsync<TOutput>(
        DomainAgent agent,
        Domain.Task.ICrewTask task,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
        where TOutput : class
    {
        ArgumentNullException.ThrowIfNull(agent);
        WarnOnce();
        if (_logger.IsEnabled(LogLevel.Trace))
            LogExecuteTaskGenericCalled(agent.Id);
        return Task.FromResult(new TaskResult<TOutput>(
            Success: false,
            RawOutput: "(no execution service configured)",
            StructuredOutput: null,
            ToolsUsed: [],
            ExecutionTime: TimeSpan.Zero,
            Error: "No IAgentExecutionService registered"));
    }

    /// <inheritdoc />
    public Task<TaskExecutionPlan> PlanTaskExecutionAsync(
        DomainAgent agent,
        Domain.Task.ICrewTask task,
        SimpleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        WarnOnce();
        return Task.FromResult(new TaskExecutionPlan(
            AssignedAgent: agent.Id,
            Steps: [],
            EstimatedDuration: TimeSpan.Zero,
            ConfidenceScore: 0.0));
    }

    /// <inheritdoc />
    public Task<bool> CanExecuteTaskAsync(
        DomainAgent agent,
        Domain.Task.ICrewTask task,
        CancellationToken cancellationToken = default)
    {
        WarnOnce();
        return Task.FromResult(false);
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Using NullAgentExecutionService — agent execution is disabled. " +
            "Register a real IAgentExecutionService to enable agent task execution.")]
    private partial void LogNullServiceFallback();

    [LoggerMessage(EventId = 2, Level = LogLevel.Trace,
        Message = "ExecuteTaskAsync called on stub for agent {AgentId}")]
    private partial void LogExecuteTaskCalled(Orkeon.Domain.Common.AgentId agentId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Trace,
        Message = "ExecuteTaskAsync<T> called on stub for agent {AgentId}")]
    private partial void LogExecuteTaskGenericCalled(Orkeon.Domain.Common.AgentId agentId);
}
