namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Interface for collecting and reporting performance metrics across the Orkeon system.
/// </summary>
public interface IPerformanceMetrics
{
    /// <summary>
    /// Records task execution metrics for an agent.
    /// </summary>
    void RecordTaskExecution(string agentId, string taskId, TimeSpan duration, bool success);

    /// <summary>
    /// Records tool usage metrics for an agent.
    /// </summary>
    void RecordToolUsage(string agentId, string toolName, TimeSpan duration);

    /// <summary>
    /// Records memory operation metrics.
    /// </summary>
    void RecordMemoryOperation(string operation, TimeSpan duration, int itemCount);

    /// <summary>
    /// Records LLM call metrics.
    /// </summary>
    void RecordLlmCall(string provider, TimeSpan duration, int tokenCount);

    /// <summary>
    /// Generates a performance report for the specified time period.
    /// </summary>
    System.Threading.Tasks.Task<PerformanceReport> GenerateReportAsync(DateTime from, DateTime toDate);

    /// <summary>
    /// Exports metrics to JSON format.
    /// </summary>
    System.Threading.Tasks.Task<string> ExportToJsonAsync(DateTime from, DateTime toDate);

    /// <summary>
    /// Exports metrics to CSV format.
    /// </summary>
    System.Threading.Tasks.Task<string> ExportToCsvAsync(DateTime from, DateTime toDate);
}

/// <summary>
/// Comprehensive performance report containing all metrics.
/// </summary>
public record PerformanceReport(
    Dictionary<string, AgentMetrics> AgentMetrics,
    Dictionary<string, ToolMetrics> ToolMetrics,
    MemoryMetrics MemoryMetrics,
    LlmMetrics LlmMetrics,
    TimeSpan TotalDuration);

/// <summary>
/// Duration statistics with percentiles, used across metrics records.
/// </summary>
public record DurationStatistics(
    TimeSpan Total,
    TimeSpan Min,
    TimeSpan Max,
    TimeSpan Avg,
    TimeSpan P95,
    TimeSpan P99)
{
    /// <summary>
    /// A zero-valued statistics instance.
    /// </summary>
    public static readonly DurationStatistics Zero = new(
        TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
        TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
}

/// <summary>
/// Metrics for individual agents.
/// </summary>
public record AgentMetrics(
    string AgentId,
    int TotalTasks,
    int SuccessfulTasks,
    int FailedTasks,
    DurationStatistics ExecutionDurations)
{
    // Backward-compatible accessors
    /// <summary>Total execution time across all tasks.</summary>
    public TimeSpan TotalExecutionTime => ExecutionDurations.Total;
    /// <summary>Minimum execution time.</summary>
    public TimeSpan MinExecutionTime => ExecutionDurations.Min;
    /// <summary>Maximum execution time.</summary>
    public TimeSpan MaxExecutionTime => ExecutionDurations.Max;
    /// <summary>Average execution time.</summary>
    public TimeSpan AvgExecutionTime => ExecutionDurations.Avg;
    /// <summary>95th percentile execution time.</summary>
    public TimeSpan P95ExecutionTime => ExecutionDurations.P95;
    /// <summary>99th percentile execution time.</summary>
    public TimeSpan P99ExecutionTime => ExecutionDurations.P99;
}

/// <summary>
/// Metrics for tool usage.
/// </summary>
public record ToolMetrics(
    string ToolName,
    int TotalCalls,
    DurationStatistics Durations)
{
    // Backward-compatible accessors
    /// <summary>Total duration across all calls.</summary>
    public TimeSpan TotalDuration => Durations.Total;
    /// <summary>Minimum call duration.</summary>
    public TimeSpan MinDuration => Durations.Min;
    /// <summary>Maximum call duration.</summary>
    public TimeSpan MaxDuration => Durations.Max;
    /// <summary>Average call duration.</summary>
    public TimeSpan AvgDuration => Durations.Avg;
    /// <summary>95th percentile call duration.</summary>
    public TimeSpan P95Duration => Durations.P95;
    /// <summary>99th percentile call duration.</summary>
    public TimeSpan P99Duration => Durations.P99;
}

/// <summary>
/// Metrics for memory operations.
/// </summary>
public record MemoryMetrics(
    long TotalOperations,
    long TotalItems,
    TimeSpan TotalDuration,
    Dictionary<string, OperationMetrics> OperationBreakdown);

/// <summary>
/// Metrics for specific operation types.
/// </summary>
public record OperationMetrics(
    string Operation,
    long Count,
    long ItemCount,
    TimeSpan TotalDuration,
    TimeSpan AvgDuration);

/// <summary>
/// Metrics for LLM provider calls.
/// </summary>
public record LlmMetrics(
    long TotalCalls,
    long TotalTokens,
    TimeSpan TotalDuration,
    Dictionary<string, ProviderMetrics> ProviderBreakdown);

/// <summary>
/// Metrics for specific LLM providers.
/// </summary>
public record ProviderMetrics(
    string Provider,
    long CallCount,
    long TokenCount,
    TimeSpan TotalDuration,
    TimeSpan AvgDuration,
    double TokensPerSecond);
