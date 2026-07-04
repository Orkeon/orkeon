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
    /// On Task Progress Async.
    /// </summary>
    public override System.Threading.Tasks.Task OnTaskProgressAsync(TaskProgressContext context, CancellationToken cancellationToken = default)
    {
        if (context == null)
            return System.Threading.Tasks.Task.CompletedTask;

        LogTaskProgress(context.TaskId, context.StepNumber, context.TotalSteps, context.ProgressPercentage, context.CurrentAction);
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

    /// <summary>
    /// On Flow Step Started Async.
    /// </summary>
    public override System.Threading.Tasks.Task OnFlowStepStartedAsync(FlowStepStartedContext context, CancellationToken cancellationToken = default)
    {
        if (context == null)
            return System.Threading.Tasks.Task.CompletedTask;

        LogFlowStepStarted(context.FlowName, context.StepName, context.StepId);
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// On Flow Step Completed Async.
    /// </summary>
    public override System.Threading.Tasks.Task OnFlowStepCompletedAsync(FlowStepCompletedContext context, CancellationToken cancellationToken = default)
    {
        if (context == null)
            return System.Threading.Tasks.Task.CompletedTask;

        LogFlowStepCompleted(context.FlowName, context.StepName, context.Success, context.Duration.TotalMilliseconds);

        if (!context.Success && context.Error != null)
        {
            LogFlowStepError(context.Error);
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Step Started: DomainAgent {AgentRole} performing {Action} with thought: {Thought}")]
    private partial void LogStepStarted(string agentRole, string action, string thought);
    [LoggerMessage(Level = LogLevel.Information, Message = "Step Completed: DomainAgent {AgentRole} action {Action} resulted in: {Observation} (Success: {Success}, Duration: {Duration}ms)")]
    private partial void LogStepCompleted(string agentRole, string action, string observation, bool success, double duration);
    [LoggerMessage(Level = LogLevel.Information, Message = "Task Started: {Description} assigned to {AgentRole}")]
    private partial void LogTaskStarted(string description, string agentRole);
    [LoggerMessage(Level = LogLevel.Information, Message = "Task Progress: {TaskId} - Step {StepNumber}/{TotalSteps} ({ProgressPercentage}%) - {CurrentAction}")]
    private partial void LogTaskProgress(string taskId, int stepNumber, int totalSteps, double progressPercentage, string currentAction);
    [LoggerMessage(Level = LogLevel.Information, Message = "Task Completed: {TaskId} (Success: {Success}, Duration: {Duration}ms, Steps: {StepsExecuted})")]
    private partial void LogTaskCompleted(string taskId, bool success, double duration, int stepsExecuted);
    [LoggerMessage(Level = LogLevel.Error, Message = "Task Error: {Error}")]
    private partial void LogTaskError(string error);
    [LoggerMessage(Level = LogLevel.Information, Message = "Flow Step Started: {FlowName} - {StepName} ({StepId})")]
    private partial void LogFlowStepStarted(string flowName, string stepName, string stepId);
    [LoggerMessage(Level = LogLevel.Information, Message = "Flow Step Completed: {FlowName} - {StepName} (Success: {Success}, Duration: {Duration}ms)")]
    private partial void LogFlowStepCompleted(string flowName, string stepName, bool success, double duration);
    [LoggerMessage(Level = LogLevel.Error, Message = "Flow Step Error: {Error}")]
    private partial void LogFlowStepError(string error);
}
