using System.Text.Json.Serialization;

namespace Orkeon.Application.Common.DTOs;

/// <summary>
/// Data Transfer Object for crew execution results.
/// Contains comprehensive information about crew execution outcomes.
/// </summary>
public sealed record CrewExecutionResultDto
{
    /// <summary>
    /// Unique identifier for the execution.
    /// </summary>
    [JsonPropertyName("execution_id")]
    public string ExecutionId { get; init; } = string.Empty;

    /// <summary>
    /// Crew identifier that was executed.
    /// </summary>
    [JsonPropertyName("crew_id")]
    public string CrewId { get; init; } = string.Empty;

    /// <summary>
    /// Final output from the crew execution.
    /// </summary>
    [JsonPropertyName("final_output")]
    public string FinalOutput { get; init; } = string.Empty;

    /// <summary>
    /// Structured final output if available.
    /// </summary>
    [JsonPropertyName("structured_output")]
    public object? StructuredOutput { get; init; }

    /// <summary>
    /// Individual task outputs.
    /// </summary>
    [JsonPropertyName("task_outputs")]
    public IReadOnlyList<TaskOutputDto> TaskOutputs { get; init; } = [];

    /// <summary>
    /// Whether the execution was successful.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// Execution start timestamp.
    /// </summary>
    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; init; }

    /// <summary>
    /// Execution completion timestamp.
    /// </summary>
    [JsonPropertyName("completed_at")]
    public DateTime CompletedAt { get; init; }

    /// <summary>
    /// Total execution duration.
    /// </summary>
    [JsonPropertyName("duration")]
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Token usage statistics.
    /// </summary>
    [JsonPropertyName("tokens_used")]
    public TokenUsageDto TokensUsed { get; init; } = new();

    /// <summary>
    /// Execution metrics and statistics.
    /// </summary>
    [JsonPropertyName("metrics")]
    public ExecutionMetricsDto Metrics { get; init; } = new();

    /// <summary>
    /// Error information if execution failed.
    /// </summary>
    [JsonPropertyName("error")]
    public ExecutionErrorDto? Error { get; init; }

    /// <summary>
    /// Execution context used.
    /// </summary>
    [JsonPropertyName("context")]
    public ExecutionContextDto? Context { get; init; }

    /// <summary>
    /// Additional metadata for extensibility.
    /// Well-known keys "crew_id", "execution_id", and "task_count" are also
    /// available as typed properties on this DTO.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; init; } = [];

    /// <summary>
    /// Number of tasks in the execution (derived from TaskOutputs).
    /// Equivalent to Metadata["task_count"] when populated by ExecutionMapper.
    /// </summary>
    public int TaskCount => TaskOutputs.Count;
}

/// <summary>
/// Data Transfer Object for batch execution results.
/// </summary>
public sealed record BatchExecutionResultDto
{
    /// <summary>
    /// Batch execution identifier.
    /// </summary>
    [JsonPropertyName("batch_id")]
    public string BatchId { get; init; } = string.Empty;

    /// <summary>
    /// Crew identifier that was executed.
    /// </summary>
    [JsonPropertyName("crew_id")]
    public string CrewId { get; init; } = string.Empty;

    /// <summary>
    /// Individual execution results.
    /// </summary>
    [JsonPropertyName("results")]
    public IReadOnlyList<CrewExecutionResultDto> Results { get; init; } = [];

    /// <summary>
    /// Number of successful executions.
    /// </summary>
    [JsonPropertyName("success_count")]
    public int SuccessCount { get; init; }

    /// <summary>
    /// Number of failed executions.
    /// </summary>
    [JsonPropertyName("failure_count")]
    public int FailureCount { get; init; }

    /// <summary>
    /// Total number of executions.
    /// </summary>
    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }

    /// <summary>
    /// Success rate (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("success_rate")]
    public double SuccessRate { get; init; }

    /// <summary>
    /// Batch execution start timestamp.
    /// </summary>
    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; init; }

    /// <summary>
    /// Batch execution completion timestamp.
    /// </summary>
    [JsonPropertyName("completed_at")]
    public DateTime CompletedAt { get; init; }

    /// <summary>
    /// Total batch execution duration.
    /// </summary>
    [JsonPropertyName("total_duration")]
    public TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Average execution time per item.
    /// </summary>
    [JsonPropertyName("average_execution_time")]
    public TimeSpan AverageExecutionTime { get; init; }

    /// <summary>
    /// Aggregated token usage statistics.
    /// </summary>
    [JsonPropertyName("total_tokens_used")]
    public TokenUsageDto TotalTokensUsed { get; init; } = new();

    /// <summary>
    /// Batch execution metrics.
    /// </summary>
    [JsonPropertyName("metrics")]
    public BatchMetricsDto Metrics { get; init; } = new();
}

/// <summary>
/// Data Transfer Object for execution metrics.
/// </summary>
public sealed record ExecutionMetricsDto
{
    /// <summary>
    /// Number of agents involved.
    /// </summary>
    [JsonPropertyName("agent_count")]
    public int AgentCount { get; init; }

    /// <summary>
    /// Number of tasks executed.
    /// </summary>
    [JsonPropertyName("task_count")]
    public int TaskCount { get; init; }

    /// <summary>
    /// Total number of tool calls.
    /// </summary>
    [JsonPropertyName("tool_call_count")]
    public int ToolCallCount { get; init; }

    /// <summary>
    /// Number of LLM requests.
    /// </summary>
    [JsonPropertyName("llm_request_count")]
    public int LlmRequestCount { get; init; }

    /// <summary>
    /// Number of memory operations.
    /// </summary>
    [JsonPropertyName("memory_operation_count")]
    public int MemoryOperationCount { get; init; }

    /// <summary>
    /// Number of delegation events.
    /// </summary>
    [JsonPropertyName("delegation_count")]
    public int DelegationCount { get; init; }

    /// <summary>
    /// Performance score (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("performance_score")]
    public double PerformanceScore { get; init; }

    /// <summary>
    /// Quality score (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("quality_score")]
    public double QualityScore { get; init; }

    /// <summary>
    /// Efficiency score (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("efficiency_score")]
    public double EfficiencyScore { get; init; }
}

/// <summary>
/// Data Transfer Object for batch execution metrics.
/// </summary>
public sealed record BatchMetricsDto
{
    /// <summary>
    /// Average performance score across all executions.
    /// </summary>
    [JsonPropertyName("average_performance_score")]
    public double AveragePerformanceScore { get; init; }

    /// <summary>
    /// Average quality score across all executions.
    /// </summary>
    [JsonPropertyName("average_quality_score")]
    public double AverageQualityScore { get; init; }

    /// <summary>
    /// Average efficiency score across all executions.
    /// </summary>
    [JsonPropertyName("average_efficiency_score")]
    public double AverageEfficiencyScore { get; init; }

    /// <summary>
    /// Total number of tasks executed in batch.
    /// </summary>
    [JsonPropertyName("total_tasks_executed")]
    public int TotalTasksExecuted { get; init; }

    /// <summary>
    /// Total number of tool calls in batch.
    /// </summary>
    [JsonPropertyName("total_tool_calls")]
    public int TotalToolCalls { get; init; }

    /// <summary>
    /// Total number of LLM requests in batch.
    /// </summary>
    [JsonPropertyName("total_llm_requests")]
    public int TotalLlmRequests { get; init; }

    /// <summary>
    /// Throughput (executions per minute).
    /// </summary>
    [JsonPropertyName("throughput")]
    public double Throughput { get; init; }

    /// <summary>
    /// Resource utilization metrics.
    /// </summary>
    [JsonPropertyName("resource_utilization")]
    public ResourceUtilizationDto? ResourceUtilization { get; init; }
}

/// <summary>
/// Data Transfer Object for execution errors.
/// </summary>
public sealed record ExecutionErrorDto
{
    /// <summary>
    /// Error code.
    /// </summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Error message.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Detailed error description.
    /// </summary>
    [JsonPropertyName("details")]
    public string? Details { get; init; }

    /// <summary>
    /// Error stack trace.
    /// </summary>
    [JsonPropertyName("stack_trace")]
    public string? StackTrace { get; init; }

    /// <summary>
    /// Inner error information.
    /// </summary>
    [JsonPropertyName("inner_error")]
    public ExecutionErrorDto? InnerError { get; init; }

    /// <summary>
    /// Error timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Agent ID where error occurred.
    /// </summary>
    [JsonPropertyName("agent_id")]
    public string? AgentId { get; init; }

    /// <summary>
    /// Task ID where error occurred.
    /// </summary>
    [JsonPropertyName("task_id")]
    public string? TaskId { get; init; }

    /// <summary>
    /// Tool name where error occurred.
    /// </summary>
    [JsonPropertyName("tool_name")]
    public string? ToolName { get; init; }

    /// <summary>
    /// Error severity level.
    /// </summary>
    [JsonPropertyName("severity")]
    public ErrorSeverity Severity { get; init; } = ErrorSeverity.Error;

    /// <summary>
    /// Whether the error is recoverable.
    /// </summary>
    [JsonPropertyName("recoverable")]
    public bool Recoverable { get; init; }

    /// <summary>
    /// Suggested recovery actions.
    /// </summary>
    [JsonPropertyName("recovery_actions")]
    public IReadOnlyList<string> RecoveryActions { get; init; } = [];
}

/// <summary>
/// Data Transfer Object for resource utilization metrics.
/// </summary>
public sealed record ResourceUtilizationDto
{
    /// <summary>
    /// Average CPU usage percentage.
    /// </summary>
    [JsonPropertyName("average_cpu_usage")]
    public double AverageCpuUsage { get; init; }

    /// <summary>
    /// Peak CPU usage percentage.
    /// </summary>
    [JsonPropertyName("peak_cpu_usage")]
    public double PeakCpuUsage { get; init; }

    /// <summary>
    /// Average memory usage in MB.
    /// </summary>
    [JsonPropertyName("average_memory_usage")]
    public double AverageMemoryUsage { get; init; }

    /// <summary>
    /// Peak memory usage in MB.
    /// </summary>
    [JsonPropertyName("peak_memory_usage")]
    public double PeakMemoryUsage { get; init; }

    /// <summary>
    /// Network I/O statistics.
    /// </summary>
    [JsonPropertyName("network_io")]
    public NetworkIoDto? NetworkIo { get; init; }

    /// <summary>
    /// Disk I/O statistics.
    /// </summary>
    [JsonPropertyName("disk_io")]
    public DiskIoDto? DiskIo { get; init; }
}

/// <summary>
/// Data Transfer Object for network I/O statistics.
/// </summary>
public sealed record NetworkIoDto
{
    /// <summary>
    /// Total bytes sent.
    /// </summary>
    [JsonPropertyName("bytes_sent")]
    public long BytesSent { get; init; }

    /// <summary>
    /// Total bytes received.
    /// </summary>
    [JsonPropertyName("bytes_received")]
    public long BytesReceived { get; init; }

    /// <summary>
    /// Number of network requests.
    /// </summary>
    [JsonPropertyName("request_count")]
    public int RequestCount { get; init; }

    /// <summary>
    /// Average request latency in milliseconds.
    /// </summary>
    [JsonPropertyName("average_latency")]
    public double AverageLatency { get; init; }
}

/// <summary>
/// Data Transfer Object for disk I/O statistics.
/// </summary>
public sealed record DiskIoDto
{
    /// <summary>
    /// Total bytes read from disk.
    /// </summary>
    [JsonPropertyName("bytes_read")]
    public long BytesRead { get; init; }

    /// <summary>
    /// Total bytes written to disk.
    /// </summary>
    [JsonPropertyName("bytes_written")]
    public long BytesWritten { get; init; }

    /// <summary>
    /// Number of read operations.
    /// </summary>
    [JsonPropertyName("read_operations")]
    public int ReadOperations { get; init; }

    /// <summary>
    /// Number of write operations.
    /// </summary>
    [JsonPropertyName("write_operations")]
    public int WriteOperations { get; init; }
}

/// <summary>
/// Error severity enumeration.
/// </summary>
public enum ErrorSeverity
{
    /// <summary>
    /// Informational message.
    /// </summary>
    Info,

    /// <summary>
    /// Warning condition.
    /// </summary>
    Warning,

    /// <summary>
    /// Error condition.
    /// </summary>
    Error,

    /// <summary>
    /// Critical error condition.
    /// </summary>
    Critical,

    /// <summary>
    /// Fatal error condition.
    /// </summary>
    Fatal
}
