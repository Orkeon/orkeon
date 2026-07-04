using Microsoft.Extensions.Logging;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Stubs;

/// <summary>
/// No-op implementation of <see cref="IStepCallback"/> that logs each event at Debug level.
/// </summary>
public sealed partial class NullStepCallback : IStepCallback
{
    private readonly ILogger<NullStepCallback> _logger;

    /// <summary>Initializes a new instance of <see cref="NullStepCallback"/>.</summary>
    public NullStepCallback(ILogger<NullStepCallback> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task OnStepStartAsync(DomainAgent agent, ICrewTask task, int iteration)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        if (_logger.IsEnabled(LogLevel.Debug))
            LogStepStarting(agent.Role, task.TaskId, iteration);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnStepCompletedAsync(DomainAgent agent, ICrewTask task, int iteration, AgentStep agentStep)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        if (_logger.IsEnabled(LogLevel.Debug))
            LogStepCompleted(agent.Role, task.TaskId, iteration);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnStepFailedAsync(DomainAgent agent, ICrewTask task, int iteration, string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(task);
        if (_logger.IsEnabled(LogLevel.Debug))
            LogStepFailed(agent.Role, task.TaskId, iteration, errorMessage);
        return Task.CompletedTask;
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Step starting: Agent={AgentRole}, Task={TaskId}, Iteration={Iteration}")]
    private partial void LogStepStarting(Orkeon.Domain.Agent.ValueObjects.AgentRole agentRole, Orkeon.Domain.Common.TaskId taskId, int iteration);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Step completed: Agent={AgentRole}, Task={TaskId}, Iteration={Iteration}")]
    private partial void LogStepCompleted(Orkeon.Domain.Agent.ValueObjects.AgentRole agentRole, Orkeon.Domain.Common.TaskId taskId, int iteration);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug,
        Message = "Step failed: Agent={AgentRole}, Task={TaskId}, Iteration={Iteration}, Error={Error}")]
    private partial void LogStepFailed(Orkeon.Domain.Agent.ValueObjects.AgentRole agentRole, Orkeon.Domain.Common.TaskId taskId, int iteration, string error);
}

/// <summary>
/// No-op implementation of <see cref="IStepProgressHandler"/> that logs at Debug level.
/// </summary>
public sealed partial class NullStepProgressHandler : IStepProgressHandler
{
    private readonly ILogger<NullStepProgressHandler> _logger;

    /// <summary>Initializes a new instance of <see cref="NullStepProgressHandler"/>.</summary>
    public NullStepProgressHandler(ILogger<NullStepProgressHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task OnStepProgressAsync(StepProgressContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_logger.IsEnabled(LogLevel.Debug))
            LogStepProgress(context.TaskId, context.AgentId, context.CurrentStep, context.TotalSteps);
        return Task.CompletedTask;
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Step progress: Task={TaskId}, Agent={AgentId}, Step={CurrentStep}/{TotalSteps}")]
    private partial void LogStepProgress(string taskId, string agentId, int currentStep, int totalSteps);
}

/// <summary>
/// No-op implementation of <see cref="ITaskCallback"/> that logs at Debug level.
/// </summary>
public sealed partial class NullTaskCallback : ITaskCallback
{
    private readonly ILogger<NullTaskCallback> _logger;

    /// <summary>Initializes a new instance of <see cref="NullTaskCallback"/>.</summary>
    public NullTaskCallback(ILogger<NullTaskCallback> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task OnTaskStartAsync(ICrewTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (_logger.IsEnabled(LogLevel.Debug))
            LogTaskStarting(task.TaskId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnTaskCompletedAsync(ICrewTask task, TaskOutput output)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (_logger.IsEnabled(LogLevel.Debug))
            LogTaskCompleted(task.TaskId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnTaskFailedAsync(ICrewTask task, string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (_logger.IsEnabled(LogLevel.Debug))
            LogTaskFailed(task.TaskId, errorMessage);
        return Task.CompletedTask;
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Task starting: {TaskId}")]
    private partial void LogTaskStarting(Orkeon.Domain.Common.TaskId taskId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Task completed: {TaskId}")]
    private partial void LogTaskCompleted(Orkeon.Domain.Common.TaskId taskId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug,
        Message = "Task failed: {TaskId}, Error={Error}")]
    private partial void LogTaskFailed(Orkeon.Domain.Common.TaskId taskId, string error);
}
