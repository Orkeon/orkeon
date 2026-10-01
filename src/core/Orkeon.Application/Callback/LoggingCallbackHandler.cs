using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Callback;

/// <summary>
/// Logging callback handler that logs all callbacks for debugging.
/// </summary>
public partial class LoggingCallbackHandler : BaseCallbackHandler
{
    private readonly ILogger<LoggingCallbackHandler> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="LoggingCallbackHandler"/>.
    /// </summary>
    public LoggingCallbackHandler(ILogger<LoggingCallbackHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// On Step Started Async.
    /// </summary>
    public override System.Threading.Tasks.Task OnStepStartedAsync(StepStartedContext context, CancellationToken cancellationToken = default)
    {
        if (context == null)
            return System.Threading.Tasks.Task.CompletedTask;

        LogStepStarted(context.AgentRole, context.Action, context.Thought);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// On Step Completed Async.
    /// </summary>
    public override System.Threading.Tasks.Task OnStepCompletedAsync(StepCompletedContext context, CancellationToken cancellationToken = default)
    {
        if (context == null)
            return System.Threading.Tasks.Task.CompletedTask;

        LogStepCompleted(context.AgentRole, context.Action, context.Observation ?? "<no observation>", context.Success, context.Duration.TotalMilliseconds);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// On Task Started Async.
    /// </summary>
    public override System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default)
    {
        if (context == null)
            return System.Threading.Tasks.Task.CompletedTask;

        LogTaskStarted(context.Description, context.AgentRole ?? "unknown");
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// On Task Completed Async.
    /// </summary>
    public override System.Threading.Tasks.Task OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
    {
        if (context == null)
            return System.Threading.Tasks.Task.CompletedTask;

        LogTaskCompleted(context.TaskId, context.Success, context.Duration.TotalMilliseconds, context.StepsExecuted);

        if (!context.Success && context.Error != null)
        {
            LogTaskError(context.Error);
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Step Started: DomainAgent {AgentRole} performing {Action} with thought: {Thought}")]
    private partial void LogStepStarted(string agentRole, string action, string thought);
    [LoggerMessage(Level = LogLevel.Information, Message = "Step Completed: DomainAgent {AgentRole} action {Action} resulted in: {Observation} (Success: {Success}, Duration: {Duration}ms)")]
    private partial void LogStepCompleted(string agentRole, string action, string observation, bool success, double duration);
    [LoggerMessage(Level = LogLevel.Information, Message = "Task Started: {Description} assigned to {AgentRole}")]
    private partial void LogTaskStarted(string description, string agentRole);
    [LoggerMessage(Level = LogLevel.Information, Message = "Task Completed: {TaskId} (Success: {Success}, Duration: {Duration}ms, Steps: {StepsExecuted})")]
    private partial void LogTaskCompleted(string taskId, bool success, double duration, int stepsExecuted);
    [LoggerMessage(Level = LogLevel.Error, Message = "Task Error: {Error}")]
    private partial void LogTaskError(string error);
}
