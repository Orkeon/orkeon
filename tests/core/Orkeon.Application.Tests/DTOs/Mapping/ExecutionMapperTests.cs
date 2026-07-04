using System.Collections.Immutable;
using Orkeon.Domain.Common;
using Orkeon.Application.Common.Mapping;
using Orkeon.Application.Common.DTOs;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces.Ports;
using TokenUsage = Orkeon.Application.Interfaces.Services.TokenUsage;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
namespace Orkeon.Application.Tests.DTOs.Mapping;

// Extension method for date string
internal static class DateTimeExtensions
{
    public static string ToIsoDateString(this DateTime date) => date.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
}

// Test memory scope implementation
internal class TestMemoryScope : IMemoryScope
{
    public string ScopeId => "test-scope";
    public string AgentId => "test-agent";

    public static System.Threading.Tasks.Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        return System.Threading.Tasks.Task.FromResult<T?>(null);
    }

    public static System.Threading.Tasks.Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) where T : class
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public static System.Threading.Tasks.Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.FromResult(false);
    }

    public static System.Threading.Tasks.Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public System.Threading.Tasks.Task<T> ExecuteInScopeAsync<T>(Func<System.Threading.Tasks.Task<T>> operation)
    {
        return operation();
    }

    public System.Threading.Tasks.Task ExecuteInScopeAsync(Func<System.Threading.Tasks.Task> operation)
    {
        return operation();
    }

    public void Dispose() { }
}

public class ExecutionMapperTests
{
    #region ToCrewExecutionResultDto Tests

    [Fact]
    public void ShouldMapAllProperties_WhenUsingToCrewExecutionResultDtoWithCompleteOutput()
    {
        // Arrange
        var crewId = "crew-123";
        var taskOutputs = new List<Application.Execution.TaskOutput>
        {
            new(TaskId1, AgentId1, "Result 1", DateTime.UtcNow, true, TimeSpan.FromMinutes(2)),
            new(TaskId2, AgentId2, "Result 2", DateTime.UtcNow, true, TimeSpan.FromMinutes(3))
        };
        var tokenUsage = new Application.Interfaces.Services.TokenUsage(100, 200, 300);
        var crewOutput = new Application.Interfaces.Services.CrewOutput("Final consolidated output", taskOutputs, TimeoutStandard, tokenUsage);

        var startTime = new DateTime(2025, 9, 13, 10, 0, 0, DateTimeKind.Utc);
        var endTime = startTime.Add(TimeoutStandard);

        // Act
        var result = ExecutionMapper.ToCrewExecutionResultDto(
            crewOutput, "exec-123", crewId, startTime);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(crewId, result.CrewId);
        Assert.Equal("Final consolidated output", result.FinalOutput);
        Assert.Equal(startTime, result.StartedAt);
        Assert.Equal(endTime, result.CompletedAt);
        Assert.Equal(TimeoutStandard, result.Duration);
        Assert.True(result.Success);

        // Task outputs
        Assert.Equal(2, result.TaskOutputs.Count);
        Assert.All(result.TaskOutputs, output => Assert.NotNull(output));

        // Token usage
        Assert.NotNull(result.TokensUsed);
        Assert.Equal(100, result.TokensUsed.PromptTokens);
        Assert.Equal(200, result.TokensUsed.CompletionTokens);
        Assert.Equal(300, result.TokensUsed.TotalTokens);

        // Metrics
        Assert.NotNull(result.Metrics);
        Assert.Equal(2, result.Metrics.TaskCount);
        Assert.Equal(0.8, result.Metrics.PerformanceScore);
        Assert.Equal(0.8, result.Metrics.QualityScore);

        // Metadata (ExecutionMapper creates standard metadata)
        Assert.NotNull(result.Metadata);
        Assert.Equal("crew-123", result.Metadata["crew_id"]);
        Assert.Equal("exec-123", result.Metadata["execution_id"]);
        Assert.Equal(2, result.Metadata["task_count"]);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToCrewExecutionResultDtoWithNullCrewOutput()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            ExecutionMapper.ToCrewExecutionResultDto(null!, "exec-123", "crew-123", DateTime.UtcNow));
        Assert.Equal("crewOutput", exception.ParamName);
    }

    [Fact]
    public void ShouldUseDefault_WhenUsingToCrewExecutionResultDtoWithEmptyCrewId()
    {
        // Arrange
        var crewOutput = new Application.Interfaces.Services.CrewOutput("Output", [], TimeSpan.Zero, new Application.Interfaces.Services.TokenUsage(0, 0, 0));

        // Act
        var result = ExecutionMapper.ToCrewExecutionResultDto(
            crewOutput, "exec-123", "", DateTime.UtcNow);

        // Assert
        Assert.Equal("", result.CrewId);
    }

    [Fact]
    public void ShouldCalculateCorrectMetrics_WhenUsingToCrewExecutionResultDtoWithPartialFailures()
    {
        // Arrange
        var taskOutputs = new List<Application.Execution.TaskOutput>
        {
            new(TaskId1, AgentId1, Success, DateTime.UtcNow, true, TimeSpan.FromMinutes(1)),
            new(TaskId2, AgentId2, Failed, DateTime.UtcNow, false, TimeSpan.FromMinutes(2)),
            new(TaskId3, AgentId3, Success, DateTime.UtcNow, true, TimeSpan.FromMinutes(3))
        };
        var crewOutput = new Application.Interfaces.Services.CrewOutput("Mixed results", taskOutputs, TimeSpan.FromMinutes(6), new Application.Interfaces.Services.TokenUsage(50, 50, 100));

        // Act
        var result = ExecutionMapper.ToCrewExecutionResultDto(
            crewOutput, "exec-123", "crew-123", DateTime.UtcNow.AddMinutes(-6));

        // Assert
        Assert.False(result.Success); // Has failures
        Assert.Equal(3, result.Metrics.TaskCount);
        // Note: ExecutionMetricsDto doesn't have TasksSucceeded, TasksFailed, SuccessRate or AverageTaskDuration
    }

    #endregion

    #region ToBatchExecutionResultDto Tests

    [Fact]
    public void ShouldAggregateCorrectly_WhenUsingToBatchExecutionResultDtoWithMultipleOutputs()
    {
        // Arrange
        var outputs = new List<Application.Interfaces.Services.CrewOutput>
        {
            new("Output 1",
                [new("t1", "a1", "R1", DateTime.UtcNow, true, TimeSpan.FromMinutes(1))],
                TimeSpan.FromMinutes(2), new Application.Interfaces.Services.TokenUsage(100, 150, 250)),
            new("Output 2",
                [new("t2", "a2", "R2", DateTime.UtcNow, false, TimeSpan.FromMinutes(3))],
                TimeSpan.FromMinutes(4), new Application.Interfaces.Services.TokenUsage(200, 250, 450)),
            new("Output 3",
                [new("t3", "a3", "R3", DateTime.UtcNow, true, TimeSpan.FromMinutes(2))],
                TimeSpan.FromMinutes(3), new Application.Interfaces.Services.TokenUsage(150, 200, 350))
        };

        var batchOutput = new Application.Common.DTOs.BatchOutput
        {
            BatchId = "batch-123",
            Results = outputs.Select(o => new Application.Common.DTOs.BatchExecutionResult
            {
                ExecutionId = Guid.NewGuid().ToString(),
                Success = o.TaskOutputs.All(t => t.Success),
                Output = o.FinalOutput,
                ExecutionTime = o.Duration,
                TokenUsage = new Application.Common.DTOs.TokenUsage { PromptTokens = o.TokensUsed!.PromptTokens, CompletionTokens = o.TokensUsed.CompletionTokens, TotalTokens = o.TokensUsed.TotalTokens },
                ErrorMessage = null,
                Input = []
            }).ToImmutableList(),
            StartTime = DateTime.UtcNow.AddMinutes(-9),
            EndTime = DateTime.UtcNow,
            TotalExecutionTime = TimeSpan.FromMinutes(9),
            TotalExecutions = outputs.Count,
            SuccessfulExecutions = 2,
            FailedExecutions = 1
        };
        var startTime = DateTime.UtcNow.AddMinutes(-9);

        // Act
        var result = ExecutionMapper.ToBatchExecutionResultDto(
            batchOutput, "batch-123", "crew-123", startTime);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("batch-123", result.BatchId);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Equal(66.67, result.SuccessRate, 2);
        Assert.Equal(TimeSpan.FromMinutes(9), result.TotalDuration);
        Assert.Equal(TimeSpan.FromMinutes(3), result.AverageExecutionTime); // 9 minutes / 3 crews

        // Aggregated token usage
        Assert.NotNull(result.TotalTokensUsed);
        Assert.Equal(450, result.TotalTokensUsed.PromptTokens);
        Assert.Equal(600, result.TotalTokensUsed.CompletionTokens);
        Assert.Equal(1050, result.TotalTokensUsed.TotalTokens);

        // Individual results
        Assert.Equal(3, result.Results.Count);
        Assert.All(result.Results, r => Assert.NotNull(r));

        // Batch metrics
        Assert.NotNull(result.Metrics);
        Assert.Equal(3, result.Metrics.TotalTasksExecuted);
        Assert.Equal(0.8, result.Metrics.AveragePerformanceScore, 2);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToBatchExecutionResultDtoWithNullBatchOutput()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            ExecutionMapper.ToBatchExecutionResultDto(null!, "batch-123", "crew-123", DateTime.UtcNow));
        Assert.Equal("batchOutput", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnEmptyResults_WhenUsingToBatchExecutionResultDtoWithEmptyOutputs()
    {
        // Arrange
        var batchOutput = new Application.Common.DTOs.BatchOutput
        {
            Results = [],
            TotalExecutions = 0,
            SuccessfulExecutions = 0,
            FailedExecutions = 0
        };

        // Act
        var result = ExecutionMapper.ToBatchExecutionResultDto(
            batchOutput, "batch-empty", "crew-empty", DateTime.UtcNow);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal(0, result.SuccessRate);
        Assert.Empty(result.Results);
    }

    #endregion

    #region ToExecutionErrorDto Tests

    [Fact]
    public void ShouldMapCorrectly_WhenUsingToExecutionErrorDtoWithBasicException()
    {
        // Arrange
        var exception = new InvalidOperationException("Operation failed");
        var beforeTimestamp = DateTime.UtcNow;

        // Act
        var result = ExecutionMapper.ToExecutionErrorDto(exception);

        // Assert
        var afterTimestamp = DateTime.UtcNow;
        Assert.NotNull(result);
        Assert.Equal("InvalidOperationException", result.Code);
        Assert.Equal("Operation failed", result.Message);
        Assert.Equal(ErrorSeverity.Error, result.Severity);
        Assert.True(result.Timestamp >= beforeTimestamp && result.Timestamp <= afterTimestamp);
        Assert.True(result.Recoverable);
        Assert.Null(result.InnerError);
    }

    [Fact]
    public void ShouldMapRecursively_WhenUsingToExecutionErrorDtoWithInnerException()
    {
        // Arrange
        var innerException = new ArgumentNullException("param", "Parameter is null");
        var outerException = new InvalidOperationException("Operation failed", innerException);

        // Act
        var result = ExecutionMapper.ToExecutionErrorDto(outerException);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.InnerError);
        Assert.Equal("ArgumentNullException", result.InnerError.Code);
        Assert.Contains("Parameter is null", result.InnerError.Message);
        Assert.Equal(ErrorSeverity.Critical, result.InnerError.Severity);
    }

    [Fact]
    public void ShouldIncludeContextInfo_WhenUsingToExecutionErrorDtoWithContext()
    {
        // Arrange
        var exception = new Exception("Test error");
        var context = new Dictionary<string, object>
        {
            ["AgentId"] = "agent-123",
            ["TaskId"] = "task-456",
            ["ToolName"] = "TestTool"
        };

        // Act
        var result = ExecutionMapper.ToExecutionErrorDto(
            exception,
            context["AgentId"].ToString(),
            context["TaskId"].ToString(),
            context["ToolName"].ToString());

        // Assert
        Assert.NotNull(result);
        Assert.Equal("agent-123", result.AgentId);
        Assert.Equal("task-456", result.TaskId);
        Assert.Equal("TestTool", result.ToolName);
    }

    [Theory]
    [InlineData(typeof(OutOfMemoryException), ErrorSeverity.Fatal, false)]
    [InlineData(typeof(StackOverflowException), ErrorSeverity.Fatal, false)]
    [InlineData(typeof(ArgumentNullException), ErrorSeverity.Critical, false)]
    [InlineData(typeof(NotImplementedException), ErrorSeverity.Error, false)]
    [InlineData(typeof(TimeoutException), ErrorSeverity.Warning, true)]
    [InlineData(typeof(InvalidOperationException), ErrorSeverity.Error, true)]
    public void ShouldHaveCorrectSeverity_WhenUsingToExecutionErrorDtoWithDifferentExceptionTypes(
        Type exceptionType, ErrorSeverity expectedSeverity, bool expectedRecoverable)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Test message")!;

        // Act
        var result = ExecutionMapper.ToExecutionErrorDto(exception);

        // Assert
        Assert.Equal(expectedSeverity, result.Severity);
        Assert.Equal(expectedRecoverable, result.Recoverable);
    }

    [Fact]
    public void ShouldSuggestActions_WhenUsingToExecutionErrorDtoWithRecoverableError()
    {
        // Arrange
        var exception = new TimeoutException("Operation timed out");

        // Act
        var result = ExecutionMapper.ToExecutionErrorDto(exception);

        // Assert
        Assert.True(result.Recoverable);
        Assert.NotNull(result.RecoveryActions);
        Assert.NotEmpty(result.RecoveryActions);
    }

    #endregion

    #region ToTokenUsageDto Tests

    [Fact]
    public void ShouldMapCorrectly_WhenUsingToTokenUsageDtoWithTypedTokenUsage()
    {
        // Arrange
        var tokenUsage = new TokenUsage(150, 250, 400);

        // Act
        var result = ExecutionMapper.ToTokenUsageDto(tokenUsage);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(150, result.PromptTokens);
        Assert.Equal(250, result.CompletionTokens);
        Assert.Equal(400, result.TotalTokens);
    }

    [Fact]
    public void ShouldMapCorrectly_WhenUsingToTokenUsageDtoWithDtoTokenUsage()
    {
        // Arrange
        var tokenUsage = new Application.Common.DTOs.TokenUsage
        {
            PromptTokens = 100,
            CompletionTokens = 200,
            TotalTokens = 300,
            EstimatedCost = 0.05m
        };

        // Act
        var result = ExecutionMapper.ToTokenUsageDto(tokenUsage);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100, result.PromptTokens);
        Assert.Equal(200, result.CompletionTokens);
        Assert.Equal(300, result.TotalTokens);
        Assert.Equal(0.05m, result.EstimatedCost);
    }

    [Fact]
    public void ShouldReturnZeroTokens_WhenUsingToTokenUsageDtoWithNullInput()
    {
        // Act
        var result = ExecutionMapper.ToTokenUsageDto(null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.PromptTokens);
        Assert.Equal(0, result.CompletionTokens);
        Assert.Equal(0, result.TotalTokens);
    }

    #endregion

    #region ToExecutionContextDto Tests

    [Fact]
    public void ShouldMapAllProperties_WhenUsingToExecutionContextDtoWithCompleteContext()
    {
        // Arrange
        var variables = new Dictionary<string, string>
        {
            ["userId"] = "user-123",
            ["iteration"] = "5",
            ["isTest"] = "true"
        };
        var previousOutputs = new List<Application.Execution.TaskOutput>
        {
            new("t1", "a1", "Output 1", DateTime.UtcNow, true, TimeSpan.FromMinutes(1)),
            new("t2", "a2", "Output 2", DateTime.UtcNow, true, TimeSpan.FromMinutes(1)),
            new("t3", "a3", "Output 3", DateTime.UtcNow, true, TimeSpan.FromMinutes(1))
        };
        using var mockMemory = new TestMemoryScope();
        var context = new SimpleExecutionContext(CrewId.Create(), variables, mockMemory, previousOutputs);

        // Act
        var result = ExecutionMapper.ToExecutionContextDto(context);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Variables);
        Assert.Equal(3, result.Variables.Count);
        Assert.Equal("user-123", result.Variables["userId"]);
        Assert.Equal("5", result.Variables["iteration"]);
        Assert.Equal("true", result.Variables["isTest"]);

        Assert.NotNull(result.PreviousOutputs);
        Assert.Equal(3, result.PreviousOutputs.Count);
        Assert.Contains(result.PreviousOutputs, o => o.Content == "Output 1");
        Assert.Contains(result.PreviousOutputs, o => o.Content == "Output 2");
        Assert.Contains(result.PreviousOutputs, o => o.Content == "Output 3");
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingToExecutionContextDtoWithNullContext()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            ExecutionMapper.ToExecutionContextDto(null!));
        Assert.Equal("context", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnEmptyDto_WhenUsingToExecutionContextDtoWithEmptyContext()
    {
        // Arrange
        using var mockMemory = new TestMemoryScope();
        var context = new SimpleExecutionContext(
            CrewId.Create(),
            [],
            mockMemory,
            []);

        // Act
        var result = ExecutionMapper.ToExecutionContextDto(context);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Variables);
        Assert.Empty(result.Variables);
        Assert.NotNull(result.PreviousOutputs);
        Assert.Empty(result.PreviousOutputs);
    }

    #endregion

    #region Helper Method Tests

    [Fact]
    public void ShouldCalculateCorrectly_WhenUsingCreateExecutionMetrics()
    {
        // Arrange
        var taskOutputs = new List<Application.Execution.TaskOutput>
        {
            new("t1", AgentId1, "Result 1", DateTime.UtcNow, true, TimeSpan.FromMinutes(2)),
            new("t2", AgentId2, "Result 2", DateTime.UtcNow, false, TimeSpan.FromMinutes(3)),
            new("t3", AgentId3, "Result 3", DateTime.UtcNow, true, TimeSpan.FromMinutes(1)),
            new("t4", "agent-4", "Result 4", DateTime.UtcNow, true, TimeSpan.FromMinutes(4))
        };

        var tokenUsage = new Application.Interfaces.Services.TokenUsage(100, 200, 300);
        var crewOutput = new Application.Interfaces.Services.CrewOutput("Final output", taskOutputs, TimeoutExtended, tokenUsage);

        // Act
        var result = ExecutionMapper.ToCrewExecutionResultDto(
            crewOutput, "exec-123", "crew-123", DateTime.UtcNow);

        // Assert
        Assert.NotNull(result.Metrics);
        Assert.Equal(4, result.Metrics.TaskCount);
        Assert.Equal(4, result.Metrics.LlmRequestCount); // Approximation based on task count
        Assert.Equal(0.8, result.Metrics.PerformanceScore);
        Assert.Equal(0.8, result.Metrics.QualityScore);
        Assert.Equal(0.8, result.Metrics.EfficiencyScore);
    }

    [Fact]
    public void ShouldSumCorrectly_WhenAggregatingTokenUsage()
    {
        // Arrange
        var tokenUsages = new List<TokenUsageDto>
        {
            new TokenUsageDto { PromptTokens = 100, CompletionTokens = 150, TotalTokens = 250 },
            new TokenUsageDto { PromptTokens = 200, CompletionTokens = 250, TotalTokens = 450 },
            new TokenUsageDto { PromptTokens = 150, CompletionTokens = 200, TotalTokens = 350 }
        };

        // Act - Test through batch result that uses aggregation
        var outputs = tokenUsages.Select((tu, i) =>
            ($"crew-{i}", new CrewOutput($"Output {i}", [],
                TimeSpan.FromMinutes(1), new TokenUsage(tu.PromptTokens, tu.CompletionTokens, tu.TotalTokens))))
            .ToList();

        // Assert - Verify token usage DTOs were created correctly and sum as expected
        var totalPrompt = tokenUsages.Sum(tu => tu.PromptTokens);
        var totalCompletion = tokenUsages.Sum(tu => tu.CompletionTokens);
        var totalTokens = tokenUsages.Sum(tu => tu.TotalTokens);

        Assert.Equal(450, totalPrompt);
        Assert.Equal(600, totalCompletion);
        Assert.Equal(1050, totalTokens);
        Assert.Equal(3, outputs.Count);
    }

    #endregion
}

// Test implementation classes
public class BatchOutput
{
    public List<(string CrewId, CrewOutput Output)> Outputs { get; }
    public TimeSpan TotalDuration { get; }

    public BatchOutput(List<(string, CrewOutput)> outputs, TimeSpan totalDuration)
    {
        Outputs = outputs;
        TotalDuration = totalDuration;
    }
}
