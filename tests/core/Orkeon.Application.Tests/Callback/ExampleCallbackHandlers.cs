using Orkeon.Application.Callback;
using Orkeon.Domain.Common;
using System.Globalization;
using SystemTask = System.Threading.Tasks.Task;

namespace Orkeon.Application.Tests.Callbacks;

// Concrete BaseCallbackHandler subclasses exercising the callback pipeline
// (contexts, overrides, composite dispatch). They used to ship inside
// Orkeon.Application as demo material; they are test support now, because the
// published assembly is not the place for example implementations.

/// <summary>
/// Example callback implementation for progress tracking and metrics.
/// </summary>
public class MetricsCallbackHandler : BaseCallbackHandler
{
    private readonly Dictionary<string, DateTime> _taskStartTimes = [];
    private readonly Dictionary<string, int> _taskStepCounts = [];
    private readonly List<TaskMetric> _taskMetrics = [];

    /// <summary>
    /// On Task Started Async.
    /// </summary>
    public override SystemTask OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        _taskStartTimes[context.TaskId] = context.Timestamp;
        _taskStepCounts[context.TaskId] = 0;
        return SystemTask.CompletedTask;
    }

    /// <summary>
    /// On Task Progress Async.
    /// </summary>
    public override SystemTask OnTaskProgressAsync(TaskProgressContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        _taskStepCounts[context.TaskId] = context.StepNumber;
        return SystemTask.CompletedTask;
    }

    /// <summary>
    /// On Task Completed Async.
    /// </summary>
    public override SystemTask OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_taskStartTimes.TryGetValue(context.TaskId, out var startTime))
        {
            var metric = new TaskMetric
            {
                TaskId = context.TaskId,
                Success = context.Success,
                Duration = context.Duration,
                StepsExecuted = context.StepsExecuted,
                StartTime = startTime,
                EndTime = context.Timestamp
            };

            _taskMetrics.Add(metric);

            // Cleanup
            _taskStartTimes.Remove(context.TaskId);
            _taskStepCounts.Remove(context.TaskId);
        }

        return SystemTask.CompletedTask;
    }

    /// <summary>
    /// Get Metrics.
    /// </summary>
    public IReadOnlyList<TaskMetric> GetMetrics() => _taskMetrics.AsReadOnly();

    /// <summary>
    /// Get Latest Metric.
    /// </summary>
    public TaskMetric? GetLatestMetric() => _taskMetrics.LastOrDefault();

    /// <summary>
    /// Get Average Execution Time.
    /// </summary>
    public double GetAverageExecutionTime() => _taskMetrics.Count > 0 ? _taskMetrics.Average(m => m.Duration.TotalSeconds) : 0;

    /// <summary>
    /// Get Success Rate.
    /// </summary>
    public double GetSuccessRate() => _taskMetrics.Count > 0 ? (double)_taskMetrics.Count(m => m.Success) / _taskMetrics.Count : 0;
}

/// <summary>
/// Task execution metric.
/// </summary>
public record TaskMetric
{
    /// <summary>Gets or sets the task id.</summary>
    public required string TaskId { get; init; }
    /// <summary>
    /// Gets or sets a value indicating whether success.
    /// </summary>
    public required bool Success { get; init; }
    /// <summary>Gets or sets the duration.</summary>
    public required TimeSpan Duration { get; init; }
    /// <summary>Gets or sets the steps executed.</summary>
    public required int StepsExecuted { get; init; }
    /// <summary>Gets or sets the start time.</summary>
    public required DateTime StartTime { get; init; }
    /// <summary>Gets or sets the end time.</summary>
    public required DateTime EndTime { get; init; }
}

/// <summary>
/// Example callback for custom business logic during task execution.
/// </summary>
public class BusinessLogicCallbackHandler : BaseCallbackHandler
{
    private readonly List<string> _executionLog = [];

    /// <summary>
    /// On Task Started Async.
    /// </summary>
    public override SystemTask OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return OnTaskStartedCoreAsync();

        async SystemTask OnTaskStartedCoreAsync()
        {
            _executionLog.Add($"[{context.Timestamp:HH:mm:ss}] Task '{context.Description}' started for agent {context.AgentRole}");

            // Custom business logic here
            if (context.Description.Contains("critical", StringComparison.OrdinalIgnoreCase))
            {
                // Send notification for critical tasks
                await NotifyAsync($"Critical task started: {context.Description}", cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// On Task Completed Async.
    /// </summary>
    public override SystemTask OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return OnTaskCompletedCoreAsync();

        async SystemTask OnTaskCompletedCoreAsync()
        {
            _executionLog.Add($"[{context.Timestamp:HH:mm:ss}] Task {context.TaskId} completed - Success: {context.Success}");

            if (!context.Success)
            {
                // Handle task failures
                await HandleTaskFailureAsync(context, cancellationToken).ConfigureAwait(false);
            }
            else if (context.Duration > TimeSpan.FromMinutes(5))
            {
                // Handle slow tasks
                await HandleSlowTaskAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// On Step Completed Async.
    /// </summary>
    public override SystemTask OnStepCompletedAsync(StepCompletedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return OnStepCompletedCoreAsync();

        async SystemTask OnStepCompletedCoreAsync()
        {
            if (!context.Success)
            {
                _executionLog.Add($"[{context.Timestamp:HH:mm:ss}] Step failed: {context.Action} - {context.Observation}");

                // Custom retry logic could go here
                await HandleStepFailureAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Get Execution Log.
    /// </summary>
    public IReadOnlyList<string> GetExecutionLog() => _executionLog.AsReadOnly();

    private async SystemTask NotifyAsync(string message, CancellationToken cancellationToken)
    {
        // Simulate notification logic
        await System.Threading.Tasks.Task.Delay(50, cancellationToken).ConfigureAwait(false);
        _executionLog.Add($"[NOTIFICATION] {message}");
    }

    private async SystemTask HandleTaskFailureAsync(TaskCompletedContext context, CancellationToken cancellationToken)
    {
        // Simulate failure handling
        await System.Threading.Tasks.Task.Delay(100, cancellationToken).ConfigureAwait(false);
        _executionLog.Add($"[FAILURE_HANDLER] Task {context.TaskId} failed: {context.Error}");
    }

    private async SystemTask HandleSlowTaskAsync(TaskCompletedContext context, CancellationToken cancellationToken)
    {
        // Simulate slow task handling
        await System.Threading.Tasks.Task.Delay(50, cancellationToken).ConfigureAwait(false);
        _executionLog.Add(string.Format(CultureInfo.InvariantCulture, "[SLOW_TASK] Task {0} took {1:F1} minutes", context.TaskId, context.Duration.TotalMinutes));
    }

    private async SystemTask HandleStepFailureAsync(StepCompletedContext context, CancellationToken cancellationToken)
    {
        // Simulate step failure handling
        await System.Threading.Tasks.Task.Delay(25, cancellationToken).ConfigureAwait(false);
        _executionLog.Add($"[STEP_FAILURE] Step failed for task {context.TaskId}: {context.Action}");
    }
}

/// <summary>
/// Simple callback for console output during development.
/// </summary>
public class ConsoleCallbackHandler : BaseCallbackHandler
{
    /// <summary>
    /// On Task Started Async.
    /// </summary>
    public override SystemTask OnTaskStartedAsync(TaskStartedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        Console.WriteLine($"🚀 Task Started: {context.Description}");
        return SystemTask.CompletedTask;
    }

    /// <summary>
    /// On Task Progress Async.
    /// </summary>
    public override SystemTask OnTaskProgressAsync(TaskProgressContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        Console.WriteLine(Inv.Format($"⚡ Progress: {context.ProgressPercentage:F0}% - {context.CurrentAction}"));
        return SystemTask.CompletedTask;
    }

    /// <summary>
    /// On Task Completed Async.
    /// </summary>
    public override SystemTask OnTaskCompletedAsync(TaskCompletedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var emoji = context.Success ? "✅" : "❌";
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} Task {1}: {2:F1}s", emoji, context.Success ? "Completed" : "Failed", context.Duration.TotalSeconds));

        if (!context.Success && context.Error != null)
        {
            Console.WriteLine($"   Error: {context.Error}");
        }

        return SystemTask.CompletedTask;
    }

    /// <summary>
    /// On Step Completed Async.
    /// </summary>
    public override SystemTask OnStepCompletedAsync(StepCompletedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var emoji = context.Success ? "✓" : "✗";
        Console.WriteLine($"  {emoji} {context.Action}: {context.Observation}");
        return SystemTask.CompletedTask;
    }
}
