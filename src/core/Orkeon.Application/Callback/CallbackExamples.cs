using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Task;
using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Callback;

/// <summary>
/// Examples demonstrating how to use the Orkeon callback system.
/// Covers the classic step_callback and task callback patterns.
/// </summary>
public static partial class CallbackExamples
{
    /// <summary>
    /// Example 1: Simple agent (callbacks are applied during execution, not on the entity)
    /// </summary>
    public static DomainAgent CreateAgent()
    {
        return DomainAgent.Create(
            AgentRole.From("Research Analyst"),
            AgentGoal.From("Analyze market data and provide insights"),
            AgentBackstory.From("You are an experienced analyst with deep market knowledge.")
        // Note: Callbacks are applied through execution services, not on the agent entity
        );
    }

    /// <summary>
    /// Example 2: DomainAgent for use with callback handler
    /// </summary>
    public static DomainAgent CreateAgentForCallbacks()
    {
        return DomainAgent.Create(
            AgentRole.From("Content Writer"),
            AgentGoal.From("Create engaging content based on research"),
            AgentBackstory.From("You are a skilled writer who transforms data into compelling narratives.")
        // Note: CallbackHandler is provided through execution context, not on the agent entity
        );
    }

    /// <summary>
    /// Example 3: Task for use with callbacks
    /// </summary>
    public static CrewTask CreateTask()
    {
        return CrewTask.Create(
            TaskDescription.From("Research the latest AI trends in 2024"),
            ExpectedOutput.From("A comprehensive report on AI trends")
        // Note: Callbacks are applied through execution services, not on the task entity
        );
    }

    /// <summary>
    /// Example 3a: Create task callbacks separately for use with execution services
    /// </summary>
    public static TaskCallbacks CreateSimpleTaskCallbacks(ILogger logger)
    {
        return TaskCallbacks.CreateActions(
            onStarted: (ctx) => CallbackExamplesLog.LogStartingTask(logger, ctx.Description),
            onProgress: (ctx) => CallbackExamplesLog.LogProgress(logger, ctx.ProgressPercentage),
            onCompleted: (ctx) => CallbackExamplesLog.LogTaskCompletedDuration(logger, ctx.Duration.TotalSeconds),
            onFailed: (ctx) => CallbackExamplesLog.LogTaskFailed(logger, ctx.Error),
            onFinally: (ctx) => CallbackExamplesLog.LogTaskFinished(logger, ctx.Success)
        );
    }

    /// <summary>
    /// Example 4: Create async task callbacks for use with execution services
    /// </summary>
    public static TaskCallbacks CreateAsyncTaskCallbacks(ILogger logger)
    {
        return TaskCallbacks.Create(
            onStarted: async (ctx) =>
            {
                CallbackExamplesLog.LogAnalyticsTaskStarted(logger, ctx.Description);
                await System.Threading.Tasks.Task.Delay(100).ConfigureAwait(false);
                CallbackExamplesLog.LogAnalyticsSystemsInitialized(logger);
            },
            onProgress: async (ctx) =>
            {
                await SaveProgressToDatabase(ctx).ConfigureAwait(false);
                CallbackExamplesLog.LogProgressSaved(logger, ctx.ProgressPercentage);
            },
            onCompleted: async (ctx) =>
            {
                CallbackExamplesLog.LogAnalysisCompleted(logger, ctx.Duration);
                await SendCompletionNotification().ConfigureAwait(false);
            },
            onFailed: async (ctx) =>
            {
                CallbackExamplesLog.LogAnalysisFailed(logger, ctx.Error);
                await LogFailureToMonitoring(ctx).ConfigureAwait(false);
            }
        );
    }

    /// <summary>
    /// Example 4a: Task for use with async callbacks
    /// </summary>
    public static CrewTask CreateAnalysisTask()
    {
        return CrewTask.Create(
            TaskDescription.From("Generate comprehensive market analysis"),
            ExpectedOutput.From("Detailed market analysis report")
        // Note: Callbacks are applied through execution services
        );
    }

    /// <summary>
    /// Example 5: Creating a composite callback handler
    /// </summary>
    public static ICallbackHandler CreateCompositeCallbackHandler(ILogger<LoggingCallbackHandler> logger)
    {
        var loggingHandler = new LoggingCallbackHandler(logger);
        var workflowHandler = new CustomWorkflowCallbackHandler("example-workflow", logger);

        return new CompositeCallbackHandler(
            loggingHandler,
            workflowHandler
        );
    }

    // Helper methods for async callback examples
#pragma warning disable S1172 // Parameters reserved for callback contract signature
    private static async System.Threading.Tasks.Task SaveProgressToDatabase(TaskProgressContext _context)
    {
        // Simulate database save
        await System.Threading.Tasks.Task.Delay(10).ConfigureAwait(false);
    }

    private static async System.Threading.Tasks.Task SendCompletionNotification()
    {
        // Simulate notification sending
        await System.Threading.Tasks.Task.Delay(50).ConfigureAwait(false);
    }

    private static async System.Threading.Tasks.Task LogFailureToMonitoring(TaskCompletedContext _context)
    {
        // Simulate monitoring logging
        await System.Threading.Tasks.Task.Delay(25).ConfigureAwait(false);
    }
#pragma warning restore S1172
}
/// <summary>
/// Example 6: Custom callback handler for specific use case.
/// </summary>
public partial class CustomWorkflowCallbackHandler : BaseCallbackHandler
{
    private readonly List<string> _workflowSteps = [];
    private readonly string _workflowId;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CustomWorkflowCallbackHandler"/>.
    /// </summary>
    public CustomWorkflowCallbackHandler(string workflowId, ILogger? logger = null)
    {
        _workflowId = workflowId;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }

    /// <summary>
    /// On Task Started Async.
    /// </summary>
    public override System.Threading.Tasks.Task OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return OnTaskStartedCoreAsync();

        async System.Threading.Tasks.Task OnTaskStartedCoreAsync()
        {
            _workflowSteps.Add($"Task Started: {context.Description}");

            await InitializeWorkflowResources(cancellationToken).ConfigureAwait(false);

            LogWorkflowTaskInitialized(_workflowId, context.TaskId);
        }
    }

    /// <summary>
    /// On Task Completed Async.
    /// </summary>
    public override System.Threading.Tasks.Task OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return OnTaskCompletedCoreAsync();

        async System.Threading.Tasks.Task OnTaskCompletedCoreAsync()
        {
            _workflowSteps.Add($"Task Completed: {context.Success} in {context.Duration}");

            if (context.Success)
            {
                await ProcessSuccessfulCompletion(context, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await HandleWorkflowFailure(context, cancellationToken).ConfigureAwait(false);
            }

            LogWorkflowTaskFinished(_workflowId, context.TaskId);
        }
    }

    /// <summary>
    /// Get Workflow Steps.
    /// </summary>
    public IReadOnlyList<string> GetWorkflowSteps() => _workflowSteps.AsReadOnly();

    [LoggerMessage(Level = LogLevel.Information, Message = "Workflow {WorkflowId}: Task {TaskId} initialized")]
    private partial void LogWorkflowTaskInitialized(string workflowId, string taskId);
    [LoggerMessage(Level = LogLevel.Information, Message = "Workflow {WorkflowId}: Task {TaskId} finished")]
    private partial void LogWorkflowTaskFinished(string workflowId, string taskId);

    private static async System.Threading.Tasks.Task InitializeWorkflowResources(CancellationToken cancellationToken)
    {
        // Simulate resource initialization
        await System.Threading.Tasks.Task.Delay(50, cancellationToken).ConfigureAwait(false);
    }

#pragma warning disable S1172 // Parameters reserved for callback contract signature
    private static async System.Threading.Tasks.Task ProcessSuccessfulCompletion(TaskCompletedContext _context, CancellationToken cancellationToken)
    {
        // Simulate success processing
        await System.Threading.Tasks.Task.Delay(25, cancellationToken).ConfigureAwait(false);
    }

    private static async System.Threading.Tasks.Task HandleWorkflowFailure(TaskCompletedContext _context, CancellationToken cancellationToken)
    {
        // Simulate failure handling
        await System.Threading.Tasks.Task.Delay(100, cancellationToken).ConfigureAwait(false);
    }
#pragma warning restore S1172
}

internal static partial class CallbackExamplesLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting task: {Description}")]
    public static partial void LogStartingTask(ILogger logger, string description);
    [LoggerMessage(Level = LogLevel.Information, Message = "Progress: {Progress}%")]
    public static partial void LogProgress(ILogger logger, double progress);
    [LoggerMessage(Level = LogLevel.Information, Message = "Task completed in {Duration}s")]
    public static partial void LogTaskCompletedDuration(ILogger logger, double duration);
    [LoggerMessage(Level = LogLevel.Error, Message = "Task failed: {Error}")]
    public static partial void LogTaskFailed(ILogger logger, string? error);
    [LoggerMessage(Level = LogLevel.Information, Message = "Task finished (Success: {Success})")]
    public static partial void LogTaskFinished(ILogger logger, bool success);
    [LoggerMessage(Level = LogLevel.Information, Message = "Analytics Task Started: {Description}")]
    public static partial void LogAnalyticsTaskStarted(ILogger logger, string description);
    [LoggerMessage(Level = LogLevel.Information, Message = "Analytics systems initialized")]
    public static partial void LogAnalyticsSystemsInitialized(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Progress saved: {Progress}%")]
    public static partial void LogProgressSaved(ILogger logger, double progress);
    [LoggerMessage(Level = LogLevel.Information, Message = "Analysis completed! Duration: {Duration}")]
    public static partial void LogAnalysisCompleted(ILogger logger, TimeSpan duration);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Analysis failed: {Error}")]
    public static partial void LogAnalysisFailed(ILogger logger, string? error);
}
