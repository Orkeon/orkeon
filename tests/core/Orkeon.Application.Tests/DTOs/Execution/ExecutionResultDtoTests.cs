using Orkeon.Application.Common.DTOs;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.DTOs.Execution;

public class ExecutionResultDtoTests
{
    #region CrewExecutionResultDto Tests

    [Fact]
    public void ShouldCreateValidDto_WhenUsingCrewExecutionResultDtoWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new CrewExecutionResultDto
        {
            ExecutionId = "exec-123",
            CrewId = "crew-456",
            FinalOutput = "Execution completed successfully",
            Success = true,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            CompletedAt = DateTime.UtcNow,
            Duration = TimeoutExtended
        };

        // Assert
        Assert.Equal("exec-123", dto.ExecutionId);
        Assert.Equal("crew-456", dto.CrewId);
        Assert.Equal("Execution completed successfully", dto.FinalOutput);
        Assert.True(dto.Success);
        Assert.Equal(TimeoutExtended, dto.Duration);
        Assert.Null(dto.StructuredOutput);
        Assert.Empty(dto.TaskOutputs);
        Assert.NotNull(dto.TokensUsed);
        Assert.NotNull(dto.Metrics);
        Assert.Null(dto.Error);
        Assert.Null(dto.Context);
        Assert.Empty(dto.Metadata);
    }

    [Fact]
    public void ShouldSetCorrectly_WhenUsingCrewExecutionResultDtoWithAllProperties()
    {
        // Arrange
        var taskOutputs = new List<TaskOutputDto>
        {
            new() { TaskId = TaskId1, AgentId = AgentId1, Content = "Output 1", Success = true },
            new() { TaskId = TaskId2, AgentId = AgentId2, Content = "Output 2", Success = true }
        };

        var tokenUsage = new TokenUsageDto
        {
            PromptTokens = 500,
            CompletionTokens = 200,
            TotalTokens = 700,
            EstimatedCost = 0.05m
        };

        var metrics = new ExecutionMetricsDto
        {
            AgentCount = 3,
            TaskCount = 5,
            ToolCallCount = 10,
            LlmRequestCount = 8,
            PerformanceScore = 0.95
        };

        var error = new ExecutionErrorDto
        {
            Code = "ERR_001",
            Message = "Minor issue occurred",
            Severity = ErrorSeverity.Warning
        };

        var context = new ExecutionContextDto
        {
            Id = "ctx-789",
            SessionId = "session-123"
        };

        var metadata = new Dictionary<string, object>
        {
            { "environment", "production" },
            { "version", "1.0.0" }
        };

        // Act
        var dto = new CrewExecutionResultDto
        {
            ExecutionId = "exec-999",
            CrewId = "crew-888",
            FinalOutput = "Complex execution completed",
            StructuredOutput = new { status = "complete", score = 100 },
            TaskOutputs = taskOutputs,
            Success = true,
            StartedAt = DateTime.UtcNow.AddHours(-1),
            CompletedAt = DateTime.UtcNow,
            Duration = TimeSpan.FromHours(1),
            TokensUsed = tokenUsage,
            Metrics = metrics,
            Error = error,
            Context = context,
            Metadata = metadata
        };

        // Assert
        Assert.Equal("exec-999", dto.ExecutionId);
        Assert.Equal(2, dto.TaskOutputs.Count);
        Assert.NotNull(dto.StructuredOutput);
        Assert.Equal(700, dto.TokensUsed.TotalTokens);
        Assert.Equal(3, dto.Metrics.AgentCount);
        Assert.Equal("ERR_001", dto.Error.Code);
        Assert.Equal("ctx-789", dto.Context.Id);
        Assert.Equal(2, dto.Metadata.Count);
    }

    #endregion

    #region BatchExecutionResultDto Tests

    [Fact]
    public void ShouldCreateValidDto_WhenUsingBatchExecutionResultDtoWithRequiredProperties()
    {
        // Arrange & Act
        var dto = new BatchExecutionResultDto
        {
            BatchId = "batch-123",
            CrewId = "crew-456",
            SuccessCount = 8,
            FailureCount = 2,
            TotalCount = 10,
            SuccessRate = 0.8,
            StartedAt = DateTime.UtcNow.AddHours(-2),
            CompletedAt = DateTime.UtcNow,
            TotalDuration = TimeSpan.FromHours(2),
            AverageExecutionTime = TimeSpan.FromMinutes(12)
        };

        // Assert
        Assert.Equal("batch-123", dto.BatchId);
        Assert.Equal("crew-456", dto.CrewId);
        Assert.Equal(8, dto.SuccessCount);
        Assert.Equal(2, dto.FailureCount);
        Assert.Equal(10, dto.TotalCount);
        Assert.Equal(0.8, dto.SuccessRate);
        Assert.Equal(TimeSpan.FromHours(2), dto.TotalDuration);
        Assert.Equal(TimeSpan.FromMinutes(12), dto.AverageExecutionTime);
        Assert.Empty(dto.Results);
        Assert.NotNull(dto.TotalTokensUsed);
        Assert.NotNull(dto.Metrics);
    }

    [Fact]
    public void ShouldCalculateCorrectly_WhenUsingBatchExecutionResultDtoWithResults()
    {
        // Arrange
        var results = new List<CrewExecutionResultDto>
        {
            new() { ExecutionId = "exec-1", Success = true },
            new() { ExecutionId = "exec-2", Success = true },
            new() { ExecutionId = "exec-3", Success = false }
        };

        var batchMetrics = new BatchMetricsDto
        {
            AveragePerformanceScore = 0.85,
            AverageQualityScore = 0.9,
            TotalTasksExecuted = 15,
            Throughput = 5.0
        };

        // Act
        var dto = new BatchExecutionResultDto
        {
            BatchId = "batch-999",
            CrewId = "crew-999",
            Results = results,
            SuccessCount = 2,
            FailureCount = 1,
            TotalCount = 3,
            SuccessRate = 0.667,
            StartedAt = DateTime.UtcNow.AddMinutes(-30),
            CompletedAt = DateTime.UtcNow,
            TotalDuration = TimeSpan.FromMinutes(30),
            AverageExecutionTime = TimeoutExtended,
            Metrics = batchMetrics
        };

        // Assert
        Assert.Equal(3, dto.Results.Count);
        Assert.Equal(2, dto.SuccessCount);
        Assert.Equal(1, dto.FailureCount);
        Assert.Equal(0.667, dto.SuccessRate, 3);
        Assert.Equal(0.85, dto.Metrics.AveragePerformanceScore);
        Assert.Equal(5.0, dto.Metrics.Throughput);
    }

    #endregion

    #region TokenUsageDto Tests

    [Fact]
    public void ShouldCalculateTotal_WhenUsingTokenUsageDtoWithBasicUsage()
    {
        // Arrange & Act
        var dto = new TokenUsageDto
        {
            PromptTokens = 1000,
            CompletionTokens = 500,
            TotalTokens = 1500
        };

        // Assert
        Assert.Equal(1000, dto.PromptTokens);
        Assert.Equal(500, dto.CompletionTokens);
        Assert.Equal(1500, dto.TotalTokens);
        Assert.Null(dto.EstimatedCost);
        Assert.Empty(dto.ModelUsage);
    }

    [Fact]
    public void ShouldTrackPerModel_WhenUsingTokenUsageDtoWithModelUsage()
    {
        // Arrange
        var modelUsage = new Dictionary<string, TokenModelUsageDto>
        {
            { ModelGpt4, new TokenModelUsageDto
                {
                    Model = ModelGpt4,
                    PromptTokens = 800,
                    CompletionTokens = 400,
                    TotalTokens = 1200,
                    RequestCount = 5,
                    EstimatedCost = 0.04m
                }
            },
            { ModelGpt35Turbo, new TokenModelUsageDto
                {
                    Model = ModelGpt35Turbo,
                    PromptTokens = 200,
                    CompletionTokens = 100,
                    TotalTokens = 300,
                    RequestCount = 3,
                    EstimatedCost = 0.01m
                }
            }
        };

        // Act
        var dto = new TokenUsageDto
        {
            PromptTokens = 1000,
            CompletionTokens = 500,
            TotalTokens = 1500,
            EstimatedCost = 0.05m,
            ModelUsage = modelUsage
        };

        // Assert
        Assert.Equal(2, dto.ModelUsage.Count);
        Assert.Equal(1200, dto.ModelUsage[ModelGpt4].TotalTokens);
        Assert.Equal(300, dto.ModelUsage[ModelGpt35Turbo].TotalTokens);
        Assert.Equal(0.05m, dto.EstimatedCost);
    }

    #endregion

    #region TokenModelUsageDto Tests

    [Fact]
    public void ShouldSetCorrectly_WhenUsingTokenModelUsageDtoWithAllProperties()
    {
        // Arrange & Act
        var dto = new TokenModelUsageDto
        {
            Model = "claude-3-opus",
            PromptTokens = 2000,
            CompletionTokens = 1000,
            TotalTokens = 3000,
            RequestCount = 10,
            EstimatedCost = 0.15m
        };

        // Assert
        Assert.Equal("claude-3-opus", dto.Model);
        Assert.Equal(2000, dto.PromptTokens);
        Assert.Equal(1000, dto.CompletionTokens);
        Assert.Equal(3000, dto.TotalTokens);
        Assert.Equal(10, dto.RequestCount);
        Assert.Equal(0.15m, dto.EstimatedCost);
    }

    #endregion

    #region ExecutionMetricsDto Tests

    [Fact]
    public void ShouldSetCorrectly_WhenUsingExecutionMetricsDtoWithCounts()
    {
        // Arrange & Act
        var dto = new ExecutionMetricsDto
        {
            AgentCount = 5,
            TaskCount = 10,
            ToolCallCount = 25,
            LlmRequestCount = 15,
            MemoryOperationCount = 30,
            DelegationCount = 3
        };

        // Assert
        Assert.Equal(5, dto.AgentCount);
        Assert.Equal(10, dto.TaskCount);
        Assert.Equal(25, dto.ToolCallCount);
        Assert.Equal(15, dto.LlmRequestCount);
        Assert.Equal(30, dto.MemoryOperationCount);
        Assert.Equal(3, dto.DelegationCount);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void ShouldAcceptValidRange_WhenUsingExecutionMetricsDtoWithScores(double score)
    {
        // Arrange & Act
        var dto = new ExecutionMetricsDto
        {
            PerformanceScore = score,
            QualityScore = score,
            EfficiencyScore = score
        };

        // Assert
        Assert.Equal(score, dto.PerformanceScore);
        Assert.Equal(score, dto.QualityScore);
        Assert.Equal(score, dto.EfficiencyScore);
    }

    #endregion

    #region BatchMetricsDto Tests

    [Fact]
    public void ShouldSetCorrectly_WhenUsingBatchMetricsDtoWithAverageScores()
    {
        // Arrange & Act
        var dto = new BatchMetricsDto
        {
            AveragePerformanceScore = 0.82,
            AverageQualityScore = 0.91,
            AverageEfficiencyScore = 0.78,
            TotalTasksExecuted = 50,
            TotalToolCalls = 120,
            TotalLlmRequests = 80,
            Throughput = 10.5
        };

        // Assert
        Assert.Equal(0.82, dto.AveragePerformanceScore);
        Assert.Equal(0.91, dto.AverageQualityScore);
        Assert.Equal(0.78, dto.AverageEfficiencyScore);
        Assert.Equal(50, dto.TotalTasksExecuted);
        Assert.Equal(120, dto.TotalToolCalls);
        Assert.Equal(80, dto.TotalLlmRequests);
        Assert.Equal(10.5, dto.Throughput);
        Assert.Null(dto.ResourceUtilization);
    }

    [Fact]
    public void ShouldIncludeMetrics_WhenUsingBatchMetricsDtoWithResourceUtilization()
    {
        // Arrange
        var resourceUtil = new ResourceUtilizationDto
        {
            AverageCpuUsage = 45.5,
            PeakCpuUsage = 78.2,
            AverageMemoryUsage = 512.0,
            PeakMemoryUsage = 1024.0
        };

        // Act
        var dto = new BatchMetricsDto
        {
            AveragePerformanceScore = 0.85,
            Throughput = 8.0,
            ResourceUtilization = resourceUtil
        };

        // Assert
        Assert.NotNull(dto.ResourceUtilization);
        Assert.Equal(45.5, dto.ResourceUtilization.AverageCpuUsage);
        Assert.Equal(1024.0, dto.ResourceUtilization.PeakMemoryUsage);
    }

    #endregion

    #region ExecutionErrorDto Tests

    [Fact]
    public void ShouldCreateValidDto_WhenUsingExecutionErrorDtoWithBasicError()
    {
        // Arrange & Act
        var dto = new ExecutionErrorDto
        {
            Code = "TASK_FAILED",
            Message = "Task execution failed",
            Severity = ErrorSeverity.Error
        };

        // Assert
        Assert.Equal("TASK_FAILED", dto.Code);
        Assert.Equal("Task execution failed", dto.Message);
        Assert.Equal(ErrorSeverity.Error, dto.Severity);
        Assert.Null(dto.Details);
        Assert.Null(dto.StackTrace);
        Assert.Null(dto.InnerError);
        Assert.False(dto.Recoverable);
        Assert.Empty(dto.RecoveryActions);
        Assert.True(dto.Timestamp <= DateTime.UtcNow);
    }

    [Fact]
    public void ShouldTrackHierarchy_WhenUsingExecutionErrorDtoWithNestedError()
    {
        // Arrange
        var innerError = new ExecutionErrorDto
        {
            Code = "NETWORK_ERROR",
            Message = "Connection timeout",
            Severity = ErrorSeverity.Warning
        };

        var recoveryActions = new List<string>
        {
            "Retry the operation",
            "Check network connectivity",
            "Contact support if issue persists"
        };

        // Act
        var dto = new ExecutionErrorDto
        {
            Code = "EXTERNAL_SERVICE_ERROR",
            Message = "Failed to call external service",
            Details = "The service did not respond within the timeout period",
            StackTrace = "at ServiceCaller.CallAsync()",
            InnerError = innerError,
            AgentId = "agent-123",
            TaskId = "task-456",
            ToolName = "HttpApiTool",
            Severity = ErrorSeverity.Error,
            Recoverable = true,
            RecoveryActions = recoveryActions
        };

        // Assert
        Assert.Equal("EXTERNAL_SERVICE_ERROR", dto.Code);
        Assert.NotNull(dto.Details);
        Assert.NotNull(dto.StackTrace);
        Assert.NotNull(dto.InnerError);
        Assert.Equal("NETWORK_ERROR", dto.InnerError.Code);
        Assert.Equal("agent-123", dto.AgentId);
        Assert.Equal("task-456", dto.TaskId);
        Assert.Equal("HttpApiTool", dto.ToolName);
        Assert.True(dto.Recoverable);
        Assert.Equal(3, dto.RecoveryActions.Count);
    }

    [Theory]
    [InlineData(ErrorSeverity.Info)]
    [InlineData(ErrorSeverity.Warning)]
    [InlineData(ErrorSeverity.Error)]
    [InlineData(ErrorSeverity.Critical)]
    [InlineData(ErrorSeverity.Fatal)]
    public void ShouldBeSettable_WhenUsingExecutionErrorDtoWithDifferentSeverities(ErrorSeverity severity)
    {
        // Arrange & Act
        var dto = new ExecutionErrorDto
        {
            Code = "TEST_ERROR",
            Message = "Test message",
            Severity = severity
        };

        // Assert
        Assert.Equal(severity, dto.Severity);
    }

    #endregion

    #region ResourceUtilizationDto Tests

    [Fact]
    public void ShouldSetCorrectly_WhenUsingResourceUtilizationDtoWithCpuAndMemory()
    {
        // Arrange & Act
        var dto = new ResourceUtilizationDto
        {
            AverageCpuUsage = 35.5,
            PeakCpuUsage = 85.2,
            AverageMemoryUsage = 768.0,
            PeakMemoryUsage = 1536.0
        };

        // Assert
        Assert.Equal(35.5, dto.AverageCpuUsage);
        Assert.Equal(85.2, dto.PeakCpuUsage);
        Assert.Equal(768.0, dto.AverageMemoryUsage);
        Assert.Equal(1536.0, dto.PeakMemoryUsage);
        Assert.Null(dto.NetworkIo);
        Assert.Null(dto.DiskIo);
    }

    [Fact]
    public void ShouldIncludeIoStats_WhenUsingResourceUtilizationDtoWithNetworkAndDisk()
    {
        // Arrange
        var networkIo = new NetworkIoDto
        {
            BytesSent = 1024000,
            BytesReceived = 2048000,
            RequestCount = 150,
            AverageLatency = 25.5
        };

        var diskIo = new DiskIoDto
        {
            BytesRead = 5120000,
            BytesWritten = 1024000,
            ReadOperations = 100,
            WriteOperations = 50
        };

        // Act
        var dto = new ResourceUtilizationDto
        {
            AverageCpuUsage = 40.0,
            PeakCpuUsage = 60.0,
            AverageMemoryUsage = 512.0,
            PeakMemoryUsage = 768.0,
            NetworkIo = networkIo,
            DiskIo = diskIo
        };

        // Assert
        Assert.NotNull(dto.NetworkIo);
        Assert.Equal(1024000, dto.NetworkIo.BytesSent);
        Assert.Equal(150, dto.NetworkIo.RequestCount);
        Assert.NotNull(dto.DiskIo);
        Assert.Equal(5120000, dto.DiskIo.BytesRead);
        Assert.Equal(50, dto.DiskIo.WriteOperations);
    }

    #endregion

    #region NetworkIoDto Tests

    [Fact]
    public void ShouldSetCorrectly_WhenUsingNetworkIoDtoWithAllProperties()
    {
        // Arrange & Act
        var dto = new NetworkIoDto
        {
            BytesSent = 10485760,
            BytesReceived = 20971520,
            RequestCount = 500,
            AverageLatency = 15.75
        };

        // Assert
        Assert.Equal(10485760, dto.BytesSent);
        Assert.Equal(20971520, dto.BytesReceived);
        Assert.Equal(500, dto.RequestCount);
        Assert.Equal(15.75, dto.AverageLatency);
    }

    #endregion

    #region DiskIoDto Tests

    [Fact]
    public void ShouldSetCorrectly_WhenUsingDiskIoDtoWithAllProperties()
    {
        // Arrange & Act
        var dto = new DiskIoDto
        {
            BytesRead = 104857600,
            BytesWritten = 52428800,
            ReadOperations = 1000,
            WriteOperations = 500
        };

        // Assert
        Assert.Equal(104857600, dto.BytesRead);
        Assert.Equal(52428800, dto.BytesWritten);
        Assert.Equal(1000, dto.ReadOperations);
        Assert.Equal(500, dto.WriteOperations);
    }

    #endregion

    #region Record Equality Tests

    [Fact]
    public void ShouldWork_WhenUsingCrewExecutionResultDtoRecordingEquality()
    {
        // Arrange
        // Use shared instances for collections to ensure equality
        var sharedTaskOutputs = new List<TaskOutputDto>();
        var sharedMetadata = new Dictionary<string, object>();
        var sharedTokensUsed = new TokenUsageDto();
        var sharedMetrics = new ExecutionMetricsDto();
        var sharedStartedAt = DateTime.UtcNow;
        var sharedCompletedAt = sharedStartedAt.AddHours(1);
        var sharedDuration = sharedCompletedAt - sharedStartedAt;

        var dto1 = new CrewExecutionResultDto
        {
            ExecutionId = "exec-1",
            CrewId = CrewId1,
            FinalOutput = "Output",
            Success = true,
            TaskOutputs = sharedTaskOutputs,
            Metadata = sharedMetadata,
            TokensUsed = sharedTokensUsed,
            Metrics = sharedMetrics,
            StartedAt = sharedStartedAt,
            CompletedAt = sharedCompletedAt,
            Duration = sharedDuration,
            StructuredOutput = null,
            Error = null,
            Context = null
        };

        var dto2 = new CrewExecutionResultDto
        {
            ExecutionId = "exec-1",
            CrewId = CrewId1,
            FinalOutput = "Output",
            Success = true,
            TaskOutputs = sharedTaskOutputs,
            Metadata = sharedMetadata,
            TokensUsed = sharedTokensUsed,
            Metrics = sharedMetrics,
            StartedAt = sharedStartedAt,
            CompletedAt = sharedCompletedAt,
            Duration = sharedDuration,
            StructuredOutput = null,
            Error = null,
            Context = null
        };

        var dto3 = new CrewExecutionResultDto
        {
            ExecutionId = "exec-2",
            CrewId = CrewId1,
            FinalOutput = "Output",
            Success = true,
            TaskOutputs = sharedTaskOutputs,
            Metadata = sharedMetadata,
            TokensUsed = sharedTokensUsed,
            Metrics = sharedMetrics,
            StartedAt = sharedStartedAt,
            CompletedAt = sharedCompletedAt,
            Duration = sharedDuration,
            StructuredOutput = null,
            Error = null,
            Context = null
        };

        // Act & Assert
        Assert.Equal(dto1, dto2);
        Assert.NotEqual(dto1, dto3);
        Assert.Equal(dto1.GetHashCode(), dto2.GetHashCode());
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void ShouldBeValid_WhenUsingCompleteExecutionResultWithAllData()
    {
        // Arrange
        var startTime = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var endTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var taskOutputs = new List<TaskOutputDto>
        {
            new()
            {
                TaskId = TaskId1,
                AgentId = AgentId1,
                Content = "Research completed",
                Success = true,
                ExecutionTime = TimeoutLong,
                ToolsUsed =
                [
                    new() { ToolId = "tool-1", ToolName = "WebSearch", Success = true }
                ]
            },
            new()
            {
                TaskId = TaskId2,
                AgentId = AgentId2,
                Content = "Analysis performed",
                Success = true,
                ExecutionTime = TimeSpan.FromMinutes(20)
            }
        };

        var modelUsage = new Dictionary<string, TokenModelUsageDto>
        {
            { ModelGpt4, new TokenModelUsageDto
                {
                    Model = ModelGpt4,
                    PromptTokens = 5000,
                    CompletionTokens = 2000,
                    TotalTokens = 7000,
                    RequestCount = 25,
                    EstimatedCost = 0.30m
                }
            }
        };

        var tokenUsage = new TokenUsageDto
        {
            PromptTokens = 5000,
            CompletionTokens = 2000,
            TotalTokens = 7000,
            EstimatedCost = 0.30m,
            ModelUsage = modelUsage
        };

        var metrics = new ExecutionMetricsDto
        {
            AgentCount = 5,
            TaskCount = 10,
            ToolCallCount = 30,
            LlmRequestCount = 25,
            MemoryOperationCount = 50,
            DelegationCount = 5,
            PerformanceScore = 0.92,
            QualityScore = 0.88,
            EfficiencyScore = 0.85
        };

        // Act
        var dto = new CrewExecutionResultDto
        {
            ExecutionId = "exec-complete",
            CrewId = "crew-complete",
            FinalOutput = "Comprehensive analysis completed with actionable insights",
            StructuredOutput = new
            {
                summary = "Analysis complete",
                recommendations = new[] { "Action 1", "Action 2", "Action 3" },
                confidence = 0.95
            },
            TaskOutputs = taskOutputs,
            Success = true,
            StartedAt = startTime,
            CompletedAt = endTime,
            Duration = endTime - startTime,
            TokensUsed = tokenUsage,
            Metrics = metrics,
            Context = new ExecutionContextDto
            {
                Id = "ctx-complete",
                SessionId = "session-complete",
                Variables = new Dictionary<string, object> { { "mode", "production" } }
            },
            Metadata = new Dictionary<string, object>
            {
                { "client", "enterprise" },
                { "priority", "high" },
                { "region", "us-west-2" }
            }
        };

        // Assert
        Assert.Equal("exec-complete", dto.ExecutionId);
        Assert.True(dto.Success);
        Assert.Equal(2, dto.TaskOutputs.Count);
        Assert.NotNull(dto.StructuredOutput);
        Assert.Equal(7000, dto.TokensUsed.TotalTokens);
        Assert.Equal(0.30m, dto.TokensUsed.EstimatedCost);
        Assert.Equal(5, dto.Metrics.AgentCount);
        Assert.Equal(0.92, dto.Metrics.PerformanceScore);
        Assert.Equal(3, dto.Metadata.Count);
        Assert.True(Math.Abs((TimeSpan.FromHours(2) - dto.Duration).TotalSeconds) < 1); // Allow < 1 second difference
    }

    #endregion
}
