using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Application.Agent;

/// <summary>
/// Tracks agent workload and provides metrics for load balancing and delegation decisions.
/// Implements simple workload tracking with task counts and execution times.
/// </summary>
public partial class AgentWorkloadTracker : IAgentWorkloadTracker
{
    private readonly ConcurrentDictionary<string, AgentWorkloadMetrics> _workloadMetrics = new();
    private readonly ILogger<AgentWorkloadTracker> _logger;
    private readonly TimeSpan _metricRetentionPeriod;

    /// <summary>
    /// Initializes a new instance of <see cref="AgentWorkloadTracker"/>.
    /// </summary>
    public AgentWorkloadTracker(ILogger<AgentWorkloadTracker>? logger = null, TimeSpan? metricRetentionPeriod = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AgentWorkloadTracker>.Instance;
        _metricRetentionPeriod = metricRetentionPeriod ?? MemoryDefaults.DefaultMetricRetentionPeriod;
    }

    /// <summary>
    /// Record Task Started.
    /// </summary>
    public void RecordTaskStarted(string agentId, string taskId)
    {
        var metrics = _workloadMetrics.GetOrAdd(agentId, _ => new AgentWorkloadMetrics(agentId));

        metrics.IncrementActiveTasks();
        metrics.RecordTaskStart(taskId);

        LogTaskStarted(agentId, taskId, metrics.ActiveTaskCount);
    }

    /// <summary>
    /// Record Task Completed.
    /// </summary>
    public void RecordTaskCompleted(string agentId, string taskId, TimeSpan executionTime, bool success)
    {
        if (!_workloadMetrics.TryGetValue(agentId, out var metrics))
        {
            LogUnknownAgentCompletion(agentId);
            return;
        }

        metrics.DecrementActiveTasks();
        metrics.RecordTaskCompletion(taskId, executionTime, success);

        LogTaskCompleted(agentId, taskId, executionTime.TotalMilliseconds, success);
    }

    /// <summary>
    /// Get Workload Info.
    /// </summary>
    public AgentWorkloadInfo GetWorkloadInfo(string agentId)
    {
        if (!_workloadMetrics.TryGetValue(agentId, out var metrics))
        {
            return new AgentWorkloadInfo(agentId, 0, 0, TimeSpan.Zero, 0);
        }

        var stats = metrics.GetStatistics(_metricRetentionPeriod);

        return new AgentWorkloadInfo(
            AgentId: agentId,
            ActiveTaskCount: metrics.ActiveTaskCount,
            CompletedTaskCount: stats.CompletedCount,
            AverageExecutionTime: stats.AverageExecutionTime,
            SuccessRate: stats.SuccessRate);
    }

    /// <summary>
    /// Get All Workloads.
    /// </summary>
    public IReadOnlyDictionary<string, AgentWorkloadInfo> GetAllWorkloads()
    {
        return _workloadMetrics.ToDictionary(
            kvp => kvp.Key,
            kvp => GetWorkloadInfo(kvp.Key));
    }

    /// <summary>
    /// Select Least Loaded Agent.
    /// </summary>
    public string SelectLeastLoadedAgent(IEnumerable<string> agentIds)
    {
        var candidates = agentIds.ToList();
        if (candidates.Count == 0)
        {
            throw new ArgumentException("No agents provided for selection", nameof(agentIds));
        }

        // Score agents based on workload (lower is better)
        var agentScores = candidates
            .Select(agentId =>
            {
                var workload = GetWorkloadInfo(agentId);
                // Score formula: active tasks * 10 + (1 - success rate) * 5 + avg execution time in seconds
                var score = workload.ActiveTaskCount * 10 +
                           (1 - workload.SuccessRate) * 5 +
                           workload.AverageExecutionTime.TotalSeconds;

                return new { AgentId = agentId, Score = score, Workload = workload };
            })
            .OrderBy(x => x.Score)
            .ToList();

        var selected = agentScores.First();

        LogAgentSelected(selected.AgentId, selected.Score, selected.Workload.ActiveTaskCount, selected.Workload.SuccessRate);

        return selected.AgentId;
    }

    /// <summary>
    /// Reset Metrics.
    /// </summary>
    public void ResetMetrics(string? agentId = null)
    {
        if (agentId != null)
        {
            _workloadMetrics.TryRemove(agentId, out _);
            LogMetricsResetForAgent(agentId);
        }
        else
        {
            _workloadMetrics.Clear();
            LogAllMetricsReset();
        }
    }

    /// <summary>
    /// Internal class to track agent metrics.
    /// </summary>
    private sealed class AgentWorkloadMetrics
    {
        private readonly ConcurrentDictionary<string, TaskMetrics> _taskMetrics = new();
        private int _activeTaskCount;

        /// <summary>
        /// Initializes a new instance of <see cref="AgentWorkloadMetrics"/>.
        /// </summary>
        public AgentWorkloadMetrics(string agentId)
        {
        }

        /// <summary>
        /// Active Task Count.
        /// </summary>
        public int ActiveTaskCount => _activeTaskCount;

        /// <summary>
        /// Increment Active Tasks.
        /// </summary>
        public void IncrementActiveTasks() => Interlocked.Increment(ref _activeTaskCount);
        /// <summary>
        /// Decrement Active Tasks.
        /// </summary>
        public void DecrementActiveTasks() => Interlocked.Decrement(ref _activeTaskCount);

        /// <summary>
        /// Record Task Start.
        /// </summary>
        public void RecordTaskStart(string taskId)
        {
            _taskMetrics[taskId] = new TaskMetrics
            {
                StartTime = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Record Task Completion.
        /// </summary>
        public void RecordTaskCompletion(string taskId, TimeSpan executionTime, bool success)
        {
            if (_taskMetrics.TryGetValue(taskId, out var metrics))
            {
                metrics.EndTime = DateTime.UtcNow;
                metrics.ExecutionTime = executionTime;
                metrics.Success = success;
            }
        }

        /// <summary>
        /// Get Statistics.
        /// </summary>
        public WorkloadStatistics GetStatistics(TimeSpan retentionPeriod)
        {
            var cutoffTime = DateTime.UtcNow - retentionPeriod;

            // Get completed tasks within retention period
            var completedTasks = _taskMetrics.Values
                .Where(t => t.EndTime.HasValue && t.EndTime.Value > cutoffTime)
                .ToList();

            if (completedTasks.Count == 0)
            {
                return new WorkloadStatistics(0, TimeSpan.Zero, 1.0);
            }

            var successCount = completedTasks.Count(t => t.Success);
            var totalExecutionTime = completedTasks.Sum(t => t.ExecutionTime.TotalMilliseconds);
            var averageExecutionTime = TimeSpan.FromMilliseconds(totalExecutionTime / completedTasks.Count);
            var successRate = (double)successCount / completedTasks.Count;

            // Clean up old metrics
            var oldTaskIds = _taskMetrics
                .Where(kvp => kvp.Value.EndTime.HasValue && kvp.Value.EndTime.Value <= cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var taskId in oldTaskIds)
            {
                _taskMetrics.TryRemove(taskId, out _);
            }

            return new WorkloadStatistics(completedTasks.Count, averageExecutionTime, successRate);
        }
    }

    private sealed class TaskMetrics
    {
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether success.
        /// </summary>
        public bool Success { get; set; }
    }

    private sealed record WorkloadStatistics(
        int CompletedCount,
        TimeSpan AverageExecutionTime,
        double SuccessRate);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent {AgentId} started task {TaskId}. Active tasks: {ActiveTasks}")]
    private partial void LogTaskStarted(string agentId, string taskId, int activeTasks);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot record task completion for unknown agent {AgentId}")]
    private partial void LogUnknownAgentCompletion(string agentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Agent {AgentId} completed task {TaskId} in {ExecutionTime}ms. Success: {Success}")]
    private partial void LogTaskCompleted(string agentId, string taskId, double executionTime, bool success);

    [LoggerMessage(Level = LogLevel.Information, Message = "Selected agent {AgentId} with score {Score} (active: {ActiveTasks}, success rate: {SuccessRate})")]
    private partial void LogAgentSelected(string agentId, double score, int activeTasks, double successRate);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reset workload metrics for agent {AgentId}")]
    private partial void LogMetricsResetForAgent(string agentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reset all workload metrics")]
    private partial void LogAllMetricsReset();
}

/// <summary>
/// Interface for agent workload tracking.
/// </summary>
public interface IAgentWorkloadTracker
{
    /// <summary>
    /// Records that an agent has started a task.
    /// </summary>
    void RecordTaskStarted(string agentId, string taskId);

    /// <summary>
    /// Records that an agent has completed a task.
    /// </summary>
    void RecordTaskCompleted(string agentId, string taskId, TimeSpan executionTime, bool success);

    /// <summary>
    /// Gets current workload information for an agent.
    /// </summary>
    AgentWorkloadInfo GetWorkloadInfo(string agentId);

    /// <summary>
    /// Gets workload information for all tracked agents.
    /// </summary>
    IReadOnlyDictionary<string, AgentWorkloadInfo> GetAllWorkloads();

    /// <summary>
    /// Selects the least loaded agent from a list of candidates.
    /// </summary>
    string SelectLeastLoadedAgent(IEnumerable<string> agentIds);

    /// <summary>
    /// Resets workload metrics for an agent or all agents.
    /// </summary>
    void ResetMetrics(string? agentId = null);
}

/// <summary>
/// Agent workload information.
/// </summary>
public record AgentWorkloadInfo(
    string AgentId,
    int ActiveTaskCount,
    int CompletedTaskCount,
    TimeSpan AverageExecutionTime,
    double SuccessRate);
