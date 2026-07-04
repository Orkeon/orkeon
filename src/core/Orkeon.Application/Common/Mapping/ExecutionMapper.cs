using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Context;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Application.Common.Mapping;

/// <summary>
/// Mapper for converting between execution results and DTOs.
/// </summary>
public static class ExecutionMapper
{
    /// <summary>
    /// Converts CrewOutput to CrewExecutionResultDto.
    /// </summary>
    public static CrewExecutionResultDto ToCrewExecutionResultDto(
        Application.Interfaces.Services.CrewOutput crewOutput,
        string executionId,
        string crewId,
        DateTime startedAt,
        ExecutionContextDto? context = null)
    {
        ArgumentNullException.ThrowIfNull(crewOutput);

        return new CrewExecutionResultDto
        {
            ExecutionId = executionId,
            CrewId = crewId,
            FinalOutput = crewOutput.FinalOutput,
            StructuredOutput = null, // Would be parsed if available
            TaskOutputs = crewOutput.TaskOutputs != null ? crewOutput.TaskOutputs.Select(ConvertToExecutionTaskOutputDto).ToList() : [],
            Success = crewOutput.TaskOutputs?.All(t => t.Success) ?? true, // All tasks must succeed for overall success
            StartedAt = startedAt,
            CompletedAt = startedAt.Add(crewOutput.Duration),
            Duration = crewOutput.Duration,
            TokensUsed = ToTokenUsageDto(crewOutput.TokensUsed),
            Metrics = CreateExecutionMetrics(crewOutput),
            Error = (crewOutput.TaskOutputs?.All(t => t.Success) ?? true) ? null : new ExecutionErrorDto
            {
                Code = "PartialTaskFailure",
                Message = "One or more tasks failed during execution",
                Timestamp = DateTime.UtcNow,
                Severity = ErrorSeverity.Error,
                Recoverable = true
            },
            Context = context,
            Metadata = new Dictionary<string, object>
            {
                {"crew_id", crewId},
                {"execution_id", executionId},
                {"task_count", crewOutput.TaskOutputs?.Count ?? 0}
            }
        };
    }

    /// <summary>
    /// Converts Application.Execution.TaskOutput to local TaskOutputDto.
    /// </summary>
    private static TaskOutputDto ConvertToExecutionTaskOutputDto(Application.Execution.TaskOutput output)
    {
        return TaskMapper.ToTaskOutputDto(output);
    }

    /// <summary>
    /// Converts BatchOutput to BatchExecutionResultDto.
    /// </summary>
    public static BatchExecutionResultDto ToBatchExecutionResultDto(
        BatchOutput batchOutput,
        string batchId,
        string crewId,
        DateTime startedAt)
    {
        ArgumentNullException.ThrowIfNull(batchOutput);

        var results = ConvertBatchResults(batchOutput, crewId);
        var avgExecutionTime = batchOutput.TotalExecutions > 0
            ? TimeSpan.FromMilliseconds(batchOutput.TotalExecutionTime.TotalMilliseconds / batchOutput.TotalExecutions)
            : TimeSpan.Zero;

        return new BatchExecutionResultDto
        {
            BatchId = batchOutput.BatchId,
            CrewId = crewId,
            Results = results,
            SuccessCount = batchOutput.SuccessfulExecutions,
            FailureCount = batchOutput.FailedExecutions,
            TotalCount = batchOutput.TotalExecutions,
            SuccessRate = batchOutput.TotalExecutions > 0
                ? (double)batchOutput.SuccessfulExecutions / batchOutput.TotalExecutions * 100
                : 0,
            StartedAt = batchOutput.StartTime,
            CompletedAt = batchOutput.EndTime,
            TotalDuration = batchOutput.TotalExecutionTime,
            AverageExecutionTime = avgExecutionTime,
            TotalTokensUsed = AggregateTokenUsage(results),
            Metrics = CreateBatchMetrics(results)
        };
    }

    /// <summary>
    /// Converts batch execution results to crew execution result DTOs.
    /// </summary>
    private static List<CrewExecutionResultDto> ConvertBatchResults(BatchOutput batchOutput, string crewId)
    {
        var results = new List<CrewExecutionResultDto>();

        foreach (var result in batchOutput.Results)
        {
            var dto = new CrewExecutionResultDto
            {
                ExecutionId = result.ExecutionId,
                CrewId = crewId,
                FinalOutput = result.Output?.ToString() ?? string.Empty,
                Success = result.Success,
                StartedAt = batchOutput.StartTime,
                CompletedAt = batchOutput.EndTime,
                Duration = result.ExecutionTime,
                TokensUsed = result.TokenUsage != null ? ToTokenUsageDto(result.TokenUsage) : new TokenUsageDto(),
                Error = result.Success ? null : new ExecutionErrorDto
                {
                    Code = "ExecutionError",
                    Message = result.ErrorMessage ?? "Unknown error",
                    Timestamp = DateTime.UtcNow
                },
                Metrics = new ExecutionMetricsDto
                {
                    TaskCount = 1, // Each batch result represents one execution
                    AgentCount = 1,
                    ToolCallCount = 0,
                    LlmRequestCount = 1,
                    MemoryOperationCount = 0,
                    DelegationCount = 0,
                    PerformanceScore = 0.8,
                    QualityScore = 0.8,
                    EfficiencyScore = 0.8
                },
                Metadata = result.Input?.ToDictionary() ?? new Dictionary<string, object>()
            };
            results.Add(dto);
        }

        return results;
    }

    /// <summary>
    /// Creates an execution error DTO from exception.
    /// </summary>
    public static ExecutionErrorDto ToExecutionErrorDto(
        Exception exception,
        string? agentId = null,
        string? taskId = null,
        string? toolName = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new ExecutionErrorDto
        {
            Code = exception.GetType().Name,
            Message = exception.Message,
            Details = exception.ToString(),
            StackTrace = exception.StackTrace,
            InnerError = exception.InnerException != null
                ? ToExecutionErrorDto(exception.InnerException)
                : null,
            Timestamp = DateTime.UtcNow,
            AgentId = agentId,
            TaskId = taskId,
            ToolName = toolName,
            Severity = DetermineSeverity(exception),
            Recoverable = DetermineRecoverable(exception),
            RecoveryActions = SuggestRecoveryActions(exception)
        };
    }

    /// <summary>
    /// Converts an <see cref="ITokenUsage"/> to <see cref="TokenUsageDto"/>.
    /// </summary>
    public static TokenUsageDto ToTokenUsageDto(ITokenUsage? tokenUsage)
    {
        if (tokenUsage is null)
            return new TokenUsageDto();

        if (tokenUsage is TokenUsageDto existing)
            return existing;

        return new TokenUsageDto
        {
            PromptTokens = tokenUsage.PromptTokens,
            CompletionTokens = tokenUsage.CompletionTokens,
            TotalTokens = tokenUsage.TotalTokens,
            EstimatedCost = tokenUsage is TokenUsage dtoUsage ? dtoUsage.EstimatedCost : null,
            ModelUsage = []
        };
    }

    /// <summary>
    /// Converts ExecutionContext to ExecutionContextDto.
    /// </summary>
    public static ExecutionContextDto ToExecutionContextDto(SimpleExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new ExecutionContextDto
        {
            Id = Guid.NewGuid().ToString(),
            Variables = context.Variables?.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value) ?? [],
            PreviousOutputs = context.PreviousOutputs?.Select(output => new TaskOutputDto
            {
                TaskId = output.TaskId,
                RawOutput = output.Content,
                AgentId = output.AgentId ?? string.Empty,
                CompletedAt = output.CompletedAt,
                Success = output.Success,
                StructuredOutput = null,
                ExecutionTime = output.ExecutionTime,
                ToolsUsed = []
            }).ToList() ?? [],
            MemoryContext = null, // Would be mapped from memory service
            UserInput = null, // Would be extracted from variables
            SessionId = null,
            CreatedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, object>
            {
                {"crew_id", context.CrewId?.ToString() ?? string.Empty},
                {"variable_count", context.Variables?.Count ?? 0}
            }
        };
    }

    /// <summary>
    /// Creates execution metrics from crew output.
    /// </summary>
    private static ExecutionMetricsDto CreateExecutionMetrics(Application.Interfaces.Services.CrewOutput output)
    {
        var taskCount = output.TaskOutputs?.Count ?? 0;
        var toolCallCount = 0; // Tool usage is tracked at the task level

        return new ExecutionMetricsDto
        {
            AgentCount = 1, // Would be calculated from actual agent usage
            TaskCount = taskCount,
            ToolCallCount = toolCallCount,
            LlmRequestCount = taskCount, // Approximation
            MemoryOperationCount = 0, // Would be tracked separately
            DelegationCount = 0, // Would be tracked separately
            PerformanceScore = 0.8, // Would be calculated based on metrics
            QualityScore = 0.8, // Would be calculated based on output quality
            EfficiencyScore = 0.8 // Would be calculated based on time/resources
        };
    }

    /// <summary>
    /// Creates batch metrics from execution results.
    /// </summary>
    private static BatchMetricsDto CreateBatchMetrics(List<CrewExecutionResultDto> results)
    {
        if (results.Count == 0)
        {
            return new BatchMetricsDto();
        }

        var totalTasks = results.Sum(r => r.Metrics.TaskCount);
        var totalToolCalls = results.Sum(r => r.Metrics.ToolCallCount);
        var totalLlmRequests = results.Sum(r => r.Metrics.LlmRequestCount);

        var avgPerformance = results.Average(r => r.Metrics.PerformanceScore);
        var avgQuality = results.Average(r => r.Metrics.QualityScore);
        var avgEfficiency = results.Average(r => r.Metrics.EfficiencyScore);

        var totalDuration = results.Max(r => r.CompletedAt) - results.Min(r => r.StartedAt);
        var throughput = totalDuration.TotalMinutes > 0
            ? results.Count / totalDuration.TotalMinutes
            : 0;

        return new BatchMetricsDto
        {
            AveragePerformanceScore = avgPerformance,
            AverageQualityScore = avgQuality,
            AverageEfficiencyScore = avgEfficiency,
            TotalTasksExecuted = totalTasks,
            TotalToolCalls = totalToolCalls,
            TotalLlmRequests = totalLlmRequests,
            Throughput = throughput,
            ResourceUtilization = new ResourceUtilizationDto
            {
                AverageCpuUsage = 50, // Would be measured
                PeakCpuUsage = 80,
                AverageMemoryUsage = 512, // MB
                PeakMemoryUsage = 1024,
                NetworkIo = new NetworkIoDto
                {
                    BytesSent = 1024 * 1024, // 1MB
                    BytesReceived = 2 * 1024 * 1024, // 2MB
                    RequestCount = totalLlmRequests,
                    AverageLatency = 150 // ms
                },
                DiskIo = new DiskIoDto
                {
                    BytesRead = 512 * 1024, // 512KB
                    BytesWritten = 256 * 1024, // 256KB
                    ReadOperations = totalTasks,
                    WriteOperations = totalTasks / 2
                }
            }
        };
    }

    /// <summary>
    /// Aggregates token usage from multiple results.
    /// </summary>
    private static TokenUsageDto AggregateTokenUsage(List<CrewExecutionResultDto> results)
    {
        var totalPromptTokens = results.Sum(r => r.TokensUsed.PromptTokens);
        var totalCompletionTokens = results.Sum(r => r.TokensUsed.CompletionTokens);

        return new TokenUsageDto
        {
            PromptTokens = totalPromptTokens,
            CompletionTokens = totalCompletionTokens,
            TotalTokens = totalPromptTokens + totalCompletionTokens,
            EstimatedCost = null, // Would be calculated
            ModelUsage = []
        };
    }

    /// <summary>
    /// Determines error severity from exception type.
    /// </summary>
    private static ErrorSeverity DetermineSeverity(Exception exception)
    {
        return exception switch
        {
            // Critical issues
            ArgumentNullException => ErrorSeverity.Critical,

            // Fatal issues
            OutOfMemoryException => ErrorSeverity.Fatal,
            StackOverflowException => ErrorSeverity.Fatal,
            AccessViolationException => ErrorSeverity.Fatal,

            // Error level
            InvalidOperationException => ErrorSeverity.Error,
            NotSupportedException => ErrorSeverity.Error,
            NotImplementedException => ErrorSeverity.Error,

            // Warning level
            ArgumentException => ErrorSeverity.Warning,
            TimeoutException => ErrorSeverity.Warning,

            _ => ErrorSeverity.Error
        };
    }

    /// <summary>
    /// Determines if an error is recoverable.
    /// </summary>
    private static bool DetermineRecoverable(Exception exception)
    {
        return exception switch
        {
            // Non-recoverable
            ArgumentNullException => false,
            NotImplementedException => false,
            OutOfMemoryException => false,
            StackOverflowException => false,
            AccessViolationException => false,

            // Recoverable
            ArgumentException => true,
            InvalidOperationException => true,
            TimeoutException => true,
            NotSupportedException => true,

            _ => true
        };
    }

    /// <summary>
    /// Suggests recovery actions for an exception.
    /// </summary>
    private static List<string> SuggestRecoveryActions(Exception exception)
    {
        return exception switch
        {
            ArgumentException => ["Validate input parameters", "Check argument values"],
            InvalidOperationException => ["Check object state", "Verify preconditions"],
            TimeoutException => ["Increase timeout value", "Check network connectivity", "Retry operation"],
            OutOfMemoryException => ["Reduce memory usage", "Restart application", "Scale up resources"],
            _ => ["Check logs for details", "Retry operation", "Contact support"]
        };
    }
}
