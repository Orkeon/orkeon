using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Application.Services.Monitoring;

/// <summary>
/// High-performance metrics collector with minimal overhead and memory constraints.
/// Cross-cutting service in Common/ — currently consumed only by Agents/AgentExecutionService
/// but designed to track metrics across agents, tools, memory, and LLM calls.
/// Kept in Common/ as a cross-cutting concern; marked obsolete pending OpenTelemetry migration.
/// </summary>
[Obsolete("Use OrkeonMetrics with OpenTelemetry instead")]
public sealed class PerformanceMetricsCollector : IPerformanceMetrics
{
    private const int MaxEntries = 10000;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    private readonly ConcurrentQueue<MetricEntry> _metricsQueue = new();
    private int _entryCount;

    // Thread-safe counters
    private long _totalTaskExecutions;
    private long _totalToolUsages;
    private long _totalMemoryOperations;
    private long _totalLlmCalls;

    /// <summary>
    /// Initializes a new instance of <see cref="PerformanceMetricsCollector"/>.
    /// </summary>
    public PerformanceMetricsCollector(ILogger<PerformanceMetricsCollector>? logger = null)
    {
    }

    /// <summary>
    /// Record Task Execution.
    /// </summary>
    public void RecordTaskExecution(string agentId, string taskId, TimeSpan duration, bool success)
    {
        var entry = new TaskExecutionMetric(
            DateTime.UtcNow,
            agentId,
            taskId,
            duration,
            success);

        AddMetric(entry);
        Interlocked.Increment(ref _totalTaskExecutions);
    }

    /// <summary>
    /// Record Tool Usage.
    /// </summary>
    public void RecordToolUsage(string agentId, string toolName, TimeSpan duration)
    {
        var entry = new ToolUsageMetric(
            DateTime.UtcNow,
            agentId,
            toolName,
            duration);

        AddMetric(entry);
        Interlocked.Increment(ref _totalToolUsages);
    }

    /// <summary>
    /// Record Memory Operation.
    /// </summary>
    public void RecordMemoryOperation(string operation, TimeSpan duration, int itemCount)
    {
        var entry = new MemoryOperationMetric(
            DateTime.UtcNow,
            operation,
            duration,
            itemCount);

        AddMetric(entry);
        Interlocked.Increment(ref _totalMemoryOperations);
    }

    /// <summary>
    /// Record Llm Call.
    /// </summary>
    public void RecordLlmCall(string provider, TimeSpan duration, int tokenCount)
    {
        var entry = new LlmCallMetric(
            DateTime.UtcNow,
            provider,
            duration,
            tokenCount);

        AddMetric(entry);
        Interlocked.Increment(ref _totalLlmCalls);
    }

    /// <summary>
    /// Generate Report Async.
    /// </summary>
    public async System.Threading.Tasks.Task<PerformanceReport> GenerateReportAsync(DateTime from, DateTime toDate)
    {
        return await System.Threading.Tasks.Task.Run(() => GenerateReport(from, toDate)).ConfigureAwait(false);
    }

    /// <summary>
    /// Export To Json Async.
    /// </summary>
    public async System.Threading.Tasks.Task<string> ExportToJsonAsync(DateTime from, DateTime toDate)
    {
        var report = await GenerateReportAsync(from, toDate).ConfigureAwait(false);
        return JsonSerializer.Serialize(report, s_jsonOptions);
    }

    /// <summary>
    /// Export To Csv Async.
    /// </summary>
    public async System.Threading.Tasks.Task<string> ExportToCsvAsync(DateTime from, DateTime toDate)
    {
        var metrics = GetMetricsInRange(from, toDate);
        var csv = new StringBuilder();

        // Header
        csv.AppendLine("Timestamp,Type,AgentId,Identifier,Duration,Success,ItemCount,TokenCount");

        await System.Threading.Tasks.Task.Run(() =>
        {
            foreach (var metric in metrics)
            {
                csv.AppendLine(metric.ToCsvLine());
            }
        }).ConfigureAwait(false);

        return csv.ToString();
    }

    private void AddMetric(MetricEntry entry)
    {
        // Implement circular buffer behavior
        if (Interlocked.Increment(ref _entryCount) > MaxEntries
            && _metricsQueue.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _entryCount);
        }

        _metricsQueue.Enqueue(entry);
    }

    private PerformanceReport GenerateReport(DateTime from, DateTime to)
    {
        var metrics = GetMetricsInRange(from, to);

        // Group by type
        var taskMetrics = metrics.OfType<TaskExecutionMetric>().ToList();
        var toolMetrics = metrics.OfType<ToolUsageMetric>().ToList();
        var memoryMetrics = metrics.OfType<MemoryOperationMetric>().ToList();
        var llmMetrics = metrics.OfType<LlmCallMetric>().ToList();

        // Calculate agent metrics
        var agentMetricsDict = taskMetrics
            .GroupBy(m => m.AgentId)
            .ToDictionary(
                g => g.Key,
                g => CalculateAgentMetrics(g.Key, g.ToList()));

        // Calculate tool metrics
        var toolMetricsDict = toolMetrics
            .GroupBy(m => m.ToolName)
            .ToDictionary(
                g => g.Key,
                g => CalculateToolMetrics(g.Key, g.ToList()));

        // Calculate memory metrics
        var memoryMetricsResult = CalculateMemoryMetrics(memoryMetrics);

        // Calculate LLM metrics
        var llmMetricsResult = CalculateLlmMetrics(llmMetrics);

        var totalDuration = to - from;

        return new PerformanceReport(
            agentMetricsDict,
            toolMetricsDict,
            memoryMetricsResult,
            llmMetricsResult,
            totalDuration);
    }

    private List<MetricEntry> GetMetricsInRange(DateTime from, DateTime to)
    {
        return _metricsQueue
            .Where(m => m.Timestamp >= from && m.Timestamp <= to)
            .OrderBy(m => m.Timestamp)
            .ToList();
    }

    private AgentMetrics CalculateAgentMetrics(string agentId, List<TaskExecutionMetric> metrics)
    {
        if (metrics.Count == 0)
        {
            return new AgentMetrics(agentId, 0, 0, 0, DurationStatistics.Zero);
        }

        var durations = metrics.Select(m => m.Duration).OrderBy(d => d).ToList();

        return new AgentMetrics(
            agentId,
            metrics.Count,
            metrics.Count(m => m.Success),
            metrics.Count(m => !m.Success),
            CalculateDurationStatistics(durations));
    }

    private ToolMetrics CalculateToolMetrics(string toolName, List<ToolUsageMetric> metrics)
    {
        if (metrics.Count == 0)
        {
            return new ToolMetrics(toolName, 0, DurationStatistics.Zero);
        }

        var durations = metrics.Select(m => m.Duration).OrderBy(d => d).ToList();

        return new ToolMetrics(
            toolName,
            metrics.Count,
            CalculateDurationStatistics(durations));
    }

    private DurationStatistics CalculateDurationStatistics(List<TimeSpan> durations)
    {
        return new DurationStatistics(
            TimeSpan.FromMilliseconds(durations.Sum(d => d.TotalMilliseconds)),
            durations.First(),
            durations.Last(),
            TimeSpan.FromMilliseconds(durations.Average(d => d.TotalMilliseconds)),
            CalculatePercentile(durations, 0.95),
            CalculatePercentile(durations, 0.99));
    }

    private MemoryMetrics CalculateMemoryMetrics(List<MemoryOperationMetric> metrics)
    {
        var operationBreakdown = metrics
            .GroupBy(m => m.Operation)
            .ToDictionary(
                g => g.Key,
                g => new OperationMetrics(
                    g.Key,
                    g.Count(),
                    g.Sum(m => m.ItemCount),
                    TimeSpan.FromMilliseconds(g.Sum(m => m.Duration.TotalMilliseconds)),
                    TimeSpan.FromMilliseconds(g.Average(m => m.Duration.TotalMilliseconds))));

        return new MemoryMetrics(
            metrics.Count,
            metrics.Sum(m => m.ItemCount),
            TimeSpan.FromMilliseconds(metrics.Sum(m => m.Duration.TotalMilliseconds)),
            operationBreakdown);
    }

    private LlmMetrics CalculateLlmMetrics(List<LlmCallMetric> metrics)
    {
        var providerBreakdown = metrics
            .GroupBy(m => m.Provider)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var totalDuration = TimeSpan.FromMilliseconds(
                        g.Sum(m => m.Duration.TotalMilliseconds));
                    var totalTokens = g.Sum(m => m.TokenCount);

                    return new ProviderMetrics(
                        g.Key,
                        g.Count(),
                        totalTokens,
                        totalDuration,
                        TimeSpan.FromMilliseconds(g.Average(m => m.Duration.TotalMilliseconds)),
                        totalDuration.TotalSeconds > 0 ? totalTokens / totalDuration.TotalSeconds : 0);
                });

        return new LlmMetrics(
            metrics.Count,
            metrics.Sum(m => m.TokenCount),
            TimeSpan.FromMilliseconds(metrics.Sum(m => m.Duration.TotalMilliseconds)),
            providerBreakdown);
    }

    private TimeSpan CalculatePercentile(List<TimeSpan> sortedDurations, double percentile)
    {
        if (sortedDurations.Count == 0)
            return TimeSpan.Zero;

        var index = (int)Math.Ceiling(percentile * sortedDurations.Count) - 1;
        index = Math.Max(0, Math.Min(index, sortedDurations.Count - 1));

        return sortedDurations[index];
    }

    // Metric entry types
    private abstract record MetricEntry(DateTime Timestamp)
    {
        /// <summary>
        /// To Csv Line.
        /// </summary>
        public abstract string ToCsvLine();
    }

    private sealed record TaskExecutionMetric(
        DateTime Timestamp,
        string AgentId,
        string TaskId,
        TimeSpan Duration,
        bool Success) : MetricEntry(Timestamp)
    {
        /// <summary>
        /// To Csv Line.
        /// </summary>
        public override string ToCsvLine()
        {
            return $"{Timestamp:O},TaskExecution,{AgentId},{TaskId},{Duration.TotalMilliseconds},{Success},,";
        }
    }

    private sealed record ToolUsageMetric(
        DateTime Timestamp,
        string AgentId,
        string ToolName,
        TimeSpan Duration) : MetricEntry(Timestamp)
    {
        /// <summary>
        /// To Csv Line.
        /// </summary>
        public override string ToCsvLine()
        {
            return $"{Timestamp:O},ToolUsage,{AgentId},{ToolName},{Duration.TotalMilliseconds},,,";
        }
    }

    private sealed record MemoryOperationMetric(
        DateTime Timestamp,
        string Operation,
        TimeSpan Duration,
        int ItemCount) : MetricEntry(Timestamp)
    {
        /// <summary>
        /// To Csv Line.
        /// </summary>
        public override string ToCsvLine()
        {
            return $"{Timestamp:O},MemoryOperation,,{Operation},{Duration.TotalMilliseconds},,{ItemCount},";
        }
    }

    private sealed record LlmCallMetric(
        DateTime Timestamp,
        string Provider,
        TimeSpan Duration,
        int TokenCount) : MetricEntry(Timestamp)
    {
        /// <summary>
        /// To Csv Line.
        /// </summary>
        public override string ToCsvLine()
        {
            return $"{Timestamp:O},LlmCall,,{Provider},{Duration.TotalMilliseconds},,,{TokenCount}";
        }
    }
}
