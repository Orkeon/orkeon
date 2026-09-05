using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Execution;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using DomainTaskCategory = Orkeon.Domain.Task.TaskCategory;
using Orkeon.Domain.Constants.Resilience;

namespace Orkeon.Application.Services.TaskRouting;

/// <summary>
/// Task execution router using pattern matching.
/// Phase 3.2.1: Replaces if/else chains with modern C# pattern matching.
/// </summary>
public partial class TaskExecutionRouter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TaskExecutionRouter> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="TaskExecutionRouter"/>.
    /// </summary>
    /// <param name="serviceProvider">Resolves the executors.</param>
    /// <param name="logger">Routing logger.</param>
    /// <param name="timeProvider">
    /// Optional clock driving the per-strategy execution pacing and the reported duration.
    /// Defaults to <see cref="TimeProvider.System"/>; tests inject a controllable provider so
    /// a routing assertion never has to wait out the strategy's real delay.
    /// </param>
    public TaskExecutionRouter(
        IServiceProvider serviceProvider,
        ILogger<TaskExecutionRouter> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Routes task execution using pattern matching based on task type and characteristics.
    /// </summary>
    public System.Threading.Tasks.Task<TaskExecutionResult> RouteAndExecuteAsync(
        ICrewTask task,
        ExecutionContext<object> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        cancellationToken.ThrowIfCancellationRequested();

        return RouteAndExecuteCoreAsync();

        async System.Threading.Tasks.Task<TaskExecutionResult> RouteAndExecuteCoreAsync()
        {
            var executionStrategy = DetermineExecutionStrategy(task);

            LogRoutingTask(task.TaskId, task.GetType().Name, executionStrategy);

            return await ExecuteWithStrategyAsync(task, context, executionStrategy, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Determines execution strategy by delegating task classification to the Domain service
    /// and mapping the resulting <see cref="TaskCategory"/> to a <see cref="TaskExecutionStrategy"/>.
    /// </summary>
    private static TaskExecutionStrategy DetermineExecutionStrategy(ICrewTask task)
    {
        var category = TaskClassificationService.Classify(
            task.GetType().Name,
            task.Description?.Value,
            task.ExpectedOutput?.Value);

        return category switch
        {
            DomainTaskCategory.Research => TaskExecutionStrategy.Research,
            DomainTaskCategory.Analysis => TaskExecutionStrategy.Analysis,
            DomainTaskCategory.Writing => TaskExecutionStrategy.Writing,
            DomainTaskCategory.Coding => TaskExecutionStrategy.Coding,
            DomainTaskCategory.Review => TaskExecutionStrategy.Review,
            _ => TaskExecutionStrategy.Generic
        };
    }

    /// <summary>
    /// Executes task using pattern matching on execution strategy.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Service-boundary fault barrier: any strategy execution failure (cancellation re-thrown) is converted to a Failed TaskExecutionResult so one task cannot crash the routing pipeline.")]
    private async System.Threading.Tasks.Task<TaskExecutionResult> ExecuteWithStrategyAsync(
        ICrewTask task,
        ExecutionContext<object> context,
        TaskExecutionStrategy strategy,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            return strategy switch
            {
                TaskExecutionStrategy.Research => await ExecuteResearchTaskAsync(task, context, cancellationToken).ConfigureAwait(false),
                TaskExecutionStrategy.Analysis => await ExecuteAnalysisTaskAsync(task, context, cancellationToken).ConfigureAwait(false),
                TaskExecutionStrategy.Writing => await ExecuteWritingTaskAsync(task, cancellationToken).ConfigureAwait(false),
                TaskExecutionStrategy.Coding => await ExecuteCodingTaskAsync(task, context, cancellationToken).ConfigureAwait(false),
                TaskExecutionStrategy.Review => await ExecuteReviewTaskAsync(task, context, cancellationToken).ConfigureAwait(false),
                TaskExecutionStrategy.Generic => await ExecuteGenericTaskAsync(task, context, cancellationToken).ConfigureAwait(false),

                _ => throw new NotSupportedException($"Execution strategy {strategy} is not supported")
            };
        }
        catch (OperationCanceledException)
        {
            throw; // Re-throw cancellation exceptions
        }
        catch (Exception ex)
        {
            LogTaskExecutionFailed(ex, task.TaskId, strategy);

            return TaskExecutionResult.Failed(
                task.TaskId ?? TaskId.Create(),
                strategy,
                ex.Message,
                TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Routes executor selection using pattern matching on task and context types.
    /// </summary>
    public TExecutor? GetExecutor<TExecutor>(Type taskType, Type contextType, Type resultType)
        where TExecutor : class
    {
        return (taskType, contextType, resultType) switch
        {
            // Pattern match specific combinations
            (var t, var c, _) when t.Name.Contains("Research", StringComparison.Ordinal) && c == typeof(object)
                => _serviceProvider.GetService<TExecutor>(),

            (var t, var c, _) when t.Name.Contains("Analysis", StringComparison.Ordinal) && c == typeof(object)
                => _serviceProvider.GetService<TExecutor>(),

            // Generic fallback
            (_, _, _) => _serviceProvider.GetService<TExecutor>()
        };
    }

#pragma warning disable S1172 // Parameters reserved for uniform execution strategy signature
    private async System.Threading.Tasks.Task<TaskExecutionResult> ExecuteResearchTaskAsync(
        ICrewTask task,
        ExecutionContext<object> _context,
        CancellationToken cancellationToken)
    {
        var startTime = _timeProvider.GetTimestamp();

        // Simulate research execution with pattern matching on task properties
        var complexity = task.Description?.Value?.Length switch
        {
            < 50 => ExecutionComplexity.Simple,
            < 200 => ExecutionComplexity.Medium,
            _ => ExecutionComplexity.Complex
        };

        var duration = complexity switch
        {
            ExecutionComplexity.Simple => TimeSpan.FromSeconds(2),
            ExecutionComplexity.Medium => TimeSpan.FromSeconds(5),
            ExecutionComplexity.Complex => TimeSpan.FromSeconds(10),
            _ => ResilienceDefaults.DefaultRetryInitialDelay
        };

        await System.Threading.Tasks.Task.Delay(duration, _timeProvider, cancellationToken).ConfigureAwait(false);

        return TaskExecutionResult.CreateSuccess(
            task.TaskId,
            TaskExecutionStrategy.Research,
            $"Research completed for: {task.Description?.Value}",
            _timeProvider.GetElapsedTime(startTime));
    }

    private async System.Threading.Tasks.Task<TaskExecutionResult> ExecuteAnalysisTaskAsync(
        ICrewTask task,
        ExecutionContext<object> _context,
        CancellationToken cancellationToken)
    {
        var startTime = _timeProvider.GetTimestamp();
        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(3), _timeProvider, cancellationToken).ConfigureAwait(false);

        return TaskExecutionResult.CreateSuccess(
            task.TaskId,
            TaskExecutionStrategy.Analysis,
            $"Analysis completed for: {task.Description?.Value}",
            _timeProvider.GetElapsedTime(startTime));
    }

    private async System.Threading.Tasks.Task<TaskExecutionResult> ExecuteWritingTaskAsync(
        ICrewTask task,
        CancellationToken cancellationToken)
    {
        var startTime = _timeProvider.GetTimestamp();
        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(4), _timeProvider, cancellationToken).ConfigureAwait(false);

        return TaskExecutionResult.CreateSuccess(
            task.TaskId,
            TaskExecutionStrategy.Writing,
            $"Writing completed for: {task.Description?.Value}",
            _timeProvider.GetElapsedTime(startTime));
    }

    private async System.Threading.Tasks.Task<TaskExecutionResult> ExecuteCodingTaskAsync(
        ICrewTask task,
        ExecutionContext<object> _context,
        CancellationToken cancellationToken)
    {
        var startTime = _timeProvider.GetTimestamp();
        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(6), _timeProvider, cancellationToken).ConfigureAwait(false);

        return TaskExecutionResult.CreateSuccess(
            task.TaskId,
            TaskExecutionStrategy.Coding,
            $"Coding completed for: {task.Description?.Value}",
            _timeProvider.GetElapsedTime(startTime));
    }

    private async System.Threading.Tasks.Task<TaskExecutionResult> ExecuteReviewTaskAsync(
        ICrewTask task,
        ExecutionContext<object> _context,
        CancellationToken cancellationToken)
    {
        var startTime = _timeProvider.GetTimestamp();
        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(2), _timeProvider, cancellationToken).ConfigureAwait(false);

        return TaskExecutionResult.CreateSuccess(
            task.TaskId,
            TaskExecutionStrategy.Review,
            $"Review completed for: {task.Description?.Value}",
            _timeProvider.GetElapsedTime(startTime));
    }

    private async System.Threading.Tasks.Task<TaskExecutionResult> ExecuteGenericTaskAsync(
        ICrewTask task,
        ExecutionContext<object> _context,
        CancellationToken cancellationToken)
    {
        var startTime = _timeProvider.GetTimestamp();
        await System.Threading.Tasks.Task.Delay(ResilienceDefaults.DefaultRetryInitialDelay, _timeProvider, cancellationToken).ConfigureAwait(false);

        return TaskExecutionResult.CreateSuccess(
            task.TaskId,
            TaskExecutionStrategy.Generic,
            $"Generic execution completed for: {task.Description?.Value}",
            _timeProvider.GetElapsedTime(startTime));
    }
#pragma warning restore S1172

    [LoggerMessage(Level = LogLevel.Information, Message = "Routing task {TaskId} of type {TaskType} using strategy {Strategy}")]
    private partial void LogRoutingTask(object? taskId, string taskType, TaskExecutionStrategy strategy);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to execute task {TaskId} with strategy {Strategy}")]
    private partial void LogTaskExecutionFailed(Exception ex, object? taskId, TaskExecutionStrategy strategy);
}

/// <summary>
/// Task execution strategies determined by pattern matching.
/// </summary>
public enum TaskExecutionStrategy
{
    /// <summary>Research.</summary>
    Research,
    /// <summary>Analysis.</summary>
    Analysis,
    /// <summary>Writing.</summary>
    Writing,
    /// <summary>Coding.</summary>
    Coding,
    /// <summary>Review.</summary>
    Review,
    /// <summary>Generic.</summary>
    Generic
}

/// <summary>
/// Execution complexity levels for pattern matching.
/// </summary>
public enum ExecutionComplexity
{
    /// <summary>Simple.</summary>
    Simple,
    /// <summary>Medium.</summary>
    Medium,
    /// <summary>Complex.</summary>
    Complex
}

/// <summary>
/// Result of task execution with pattern matching routing.
/// </summary>
public record TaskExecutionResult(
    TaskId TaskId,
    TaskExecutionStrategy Strategy,
    bool Success,
    string? Output,
    string? Error,
    TimeSpan Duration)
{
    /// <summary>
    /// Create Success.
    /// </summary>
    public static TaskExecutionResult CreateSuccess(
        TaskId taskId,
        TaskExecutionStrategy strategy,
        string output,
        TimeSpan duration) =>
        new(taskId, strategy, true, output, null, duration);

    /// <summary>
    /// Failed.
    /// </summary>
    public static TaskExecutionResult Failed(
        TaskId taskId,
        TaskExecutionStrategy strategy,
        string error,
        TimeSpan duration) =>
        new(taskId, strategy, false, null, error, duration);
}
