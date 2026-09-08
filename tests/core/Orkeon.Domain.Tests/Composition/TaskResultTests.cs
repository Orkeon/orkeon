using Orkeon.Domain.Agent.Composition;

using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
namespace Orkeon.Domain.Tests.Composition;

/// <summary>
/// Tests for TaskResult following Clean Architecture principles.
/// Tests the task execution result class and its behavior.
/// </summary>
public class TaskResultTests
{
    private static readonly int[] SampleIntArray = [1, 2, 3];

    private static readonly string[] s_analysisTools = ["DataLoader", "StatisticalAnalyzer", "PatternDetector", "Visualizer"];
    private static readonly string[] s_processingTools = ["DataCollector", "Validator", "Processor", "ReportGenerator", "Logger"];
    private static readonly string[] s_warningMessages = ["500 items failed validation", "Processing took longer than expected"];

    #region Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingTaskResultWithDefaultConstructor()
    {
        // Act
        var result = new TaskResult();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Output);
        Assert.Null(result.StructuredOutput);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Equal(default(DateTime), result.StartedAt);
        Assert.Equal(default(DateTime), result.CompletedAt);
        Assert.Empty(result.ToolsUsed);
        Assert.Empty(result.Metadata);
        Assert.Null(result.Error);
        Assert.Equal(0, result.RetryCount);
        Assert.Null(result.ConfidenceScore);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingTaskResultUsingProperties()
    {
        // Arrange
        var startedAt = DateTime.UtcNow.AddMinutes(-5);
        var completedAt = DateTime.UtcNow;
        var duration = completedAt - startedAt;
        var toolsUsed = new List<string> { "WebSearch", "Calculator", "FileReader" };
        var metadata = new Dictionary<string, object>
        {
            { "iterations", 3 },
            { "model", ModelGpt4 },
            { "temperature", 0.7 }
        };

        // Act
        var result = new TaskResult
        {
            TaskId = TaskId.Create(),
            AgentId = AgentId.Create(),
            Success = true,
            Output = "Task completed successfully",
            StructuredOutput = new { result = 42, status = "complete" },
            Duration = duration,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ToolsUsed = toolsUsed,
            Metadata = metadata,
            Error = null,
            RetryCount = 2,
            ConfidenceScore = 0.95
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Task completed successfully", result.Output);
        Assert.Equal(duration, result.Duration);
        Assert.Equal(startedAt, result.StartedAt);
        Assert.Equal(completedAt, result.CompletedAt);
        Assert.Equal(toolsUsed, result.ToolsUsed);
        Assert.Equal(metadata, result.Metadata);
        Assert.Null(result.Error);
        Assert.Equal(2, result.RetryCount);
        Assert.Equal(0.95, result.ConfidenceScore);
    }

    #endregion

    #region CreateSuccess Factory Method Tests

    [Fact]
    public void ShouldCreateSuccessResult_WhenCreatingSuccessWithRequiredParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var output = "Analysis complete: 95% accuracy achieved";
        var duration = TimeSpan.FromMinutes(3.5);
        var beforeCreation = DateTime.UtcNow;

        // Act
        var result = TaskResult.CreateSuccess(taskId, agentId, output, duration);

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.Equal(taskId, result.TaskId);
        Assert.Equal(agentId, result.AgentId);
        Assert.True(result.Success);
        Assert.Equal(output, result.Output);
        Assert.Null(result.StructuredOutput);
        Assert.Equal(duration, result.Duration);
        Assert.Null(result.Error);

        // Verify timestamps
        Assert.True(result.CompletedAt >= beforeCreation);
        Assert.True(result.CompletedAt <= afterCreation);
        // Exact, not approximate: the factory reads the clock once, so StartedAt is
        // CompletedAt minus Duration to the tick. The old 10 ms tolerance was hiding two
        // clock reads, and its message formatted both sides without sub-seconds -- it
        // failed under load printing "Expected 10:38:31, Actual 10:38:31".
        Assert.Equal(result.CompletedAt.Subtract(duration), result.StartedAt);

        // Verify empty collections
        Assert.Empty(result.ToolsUsed);
        Assert.Empty(result.Metadata);
        Assert.Equal(0, result.RetryCount);
        Assert.Null(result.ConfidenceScore);
    }

    [Fact]
    public void ShouldIncludeStructuredData_WhenCreatingSuccessWithStructuredOutput()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var output = "Data processing complete";
        var duration = TimeSpan.FromSeconds(45);
        var structuredOutput = new
        {
            processedRecords = 1000,
            errors = 0,
            warnings = 3,
            results = new[] { "result1", "result2", "result3" }
        };

        // Act
        var result = TaskResult.CreateSuccess(taskId, agentId, output, duration, structuredOutput);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(output, result.Output);
        Assert.Equal(structuredOutput, result.StructuredOutput);
    }

    [Fact]
    public void ShouldAcceptAll_WhenCreatingSuccessWithVariousTaskIds()
    {
        // Act
        var taskId = TaskId.Create();
        var result = TaskResult.CreateSuccess(taskId, AgentId.Create(), "Output", TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal(taskId, result.TaskId);
        Assert.True(result.Success);
    }

    [Fact]
    public void ShouldCreateValidResult_WhenCreatingSuccessWithZeroDuration()
    {
        // Act
        var result = TaskResult.CreateSuccess(TaskId.Create(), AgentId.Create(), TaskId.Create(), TimeSpan.Zero);

        // Assert
        Assert.Equal(TimeSpan.Zero, result.Duration);
        // Allow small timing differences (less than 1 millisecond) due to DateTime.UtcNow calls
        var timeDiff = Math.Abs((result.CompletedAt - result.StartedAt).TotalMilliseconds);
        Assert.True(timeDiff < 1, $"Expected minimal time difference, but got {timeDiff}ms");
    }

    #endregion

    #region CreateFailure Factory Method Tests

    [Fact]
    public void ShouldCreateFailureResult_WhenCreatingFailureWithRequiredParameters()
    {
        // Arrange
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var error = "Failed to connect to external API: timeout after 30 seconds";
        var duration = TimeoutQuick;
        var beforeCreation = DateTime.UtcNow;

        // Act
        var result = TaskResult.CreateFailure(taskId, agentId, error, duration);

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.Equal(taskId, result.TaskId);
        Assert.Equal(agentId, result.AgentId);
        Assert.False(result.Success);
        Assert.Equal(error, result.Error);
        Assert.Equal(string.Empty, result.Output);
        Assert.Null(result.StructuredOutput);
        Assert.Equal(duration, result.Duration);

        // Verify timestamps
        Assert.True(result.CompletedAt >= beforeCreation);
        Assert.True(result.CompletedAt <= afterCreation);
        // Exact, not approximate: the factory reads the clock once, so StartedAt is
        // CompletedAt minus Duration to the tick. The old 10 ms tolerance was hiding two
        // clock reads, and its message formatted both sides without sub-seconds -- it
        // failed under load printing "Expected 10:38:31, Actual 10:38:31".
        Assert.Equal(result.CompletedAt.Subtract(duration), result.StartedAt);

        // Verify empty collections
        Assert.Empty(result.ToolsUsed);
        Assert.Empty(result.Metadata);
        Assert.Equal(0, result.RetryCount);
        Assert.Null(result.ConfidenceScore);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Simple error")]
    [InlineData("Detailed error: Failed at line 42 in module X with exception Y")]
    [InlineData("Multi-line\nerror\nmessage")]
    [InlineData(null)]
    public void ShouldAcceptAll_WhenCreatingFailureWithVariousErrors(string? error)
    {
        // Act
        var result = TaskResult.CreateFailure(TaskId.Create(), AgentId.Create(), error!, TimeSpan.FromSeconds(1));

        // Assert
        Assert.False(result.Success);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCreatingFailureWithLongDuration()
    {
        // Arrange
        var duration = TimeSpan.FromHours(24); // Very long task

        // Act
        var result = TaskResult.CreateFailure(TaskId.Create(), AgentId.Create(), TaskId.Create(), duration);

        // Assert
        Assert.Equal(duration, result.Duration);
        Assert.Equal(duration, result.CompletedAt - result.StartedAt);
    }

    #endregion

    #region Collections and Metadata Tests

    [Fact]
    public void ShouldSupportCollections_WhenUsingToolsUsed()
    {
        // Arrange & Act
        var result = new TaskResult
        {
            ToolsUsed = ["WebSearch", "Calculator", "FileReader", "WebSearch"]
        };

        // Assert
        Assert.Equal(4, result.ToolsUsed.Count);
        Assert.Equal(2, result.ToolsUsed.Count(t => t == "WebSearch"));
    }

    [Fact]
    public void ShouldSupportVariousTypes_WhenUsingMetadata()
    {
        // Arrange & Act
        var result = new TaskResult
        {
            Metadata = new Dictionary<string, object>
            {
                { "string_value", "test" },
                { "int_value", 42 },
                { "double_value", 3.14 },
                { "bool_value", true },
                { "datetime_value", DateTime.UtcNow },
                { "array_value", SampleIntArray },
                { "object_value", new { name = "test", value = 123 } },
                { "null_value", null! }
            }
        };

        // Assert
        Assert.Equal(8, result.Metadata.Count);
        Assert.Equal("test", result.Metadata["string_value"]);
        Assert.Equal(42, result.Metadata["int_value"]);
        Assert.Equal(3.14, result.Metadata["double_value"]);
        Assert.True((bool)result.Metadata["bool_value"]);
        Assert.IsType<int[]>(result.Metadata["array_value"]);
        Assert.Null(result.Metadata["null_value"]);
    }

    [Fact]
    public void ShouldOverwrite_WhenUsingMetadataUpdateExisting()
    {
        // Arrange & Act — Dictionary is mutable even with init
        var result = new TaskResult
        {
            Metadata = new Dictionary<string, object> { { "key", "original" } }
        };

        result.Metadata["key"] = "updated";

        // Assert
        Assert.Single(result.Metadata);
        Assert.Equal("updated", result.Metadata["key"]);
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ShouldBeAccepted_WhenUsingTaskResultWithNegativeDuration()
    {
        // Arrange & Act
        var result = new TaskResult { Duration = TimeSpan.FromMinutes(-5) };

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(-5), result.Duration);
    }

    [Fact]
    public void ShouldBeAccepted_WhenUsingTaskResultWithNegativeRetryCount()
    {
        // Arrange & Act
        var result = new TaskResult { RetryCount = -10 };

        // Assert
        Assert.Equal(-10, result.RetryCount);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ShouldAcceptAll_WhenUsingConfidenceScoreWithVariousValues(double score)
    {
        // Arrange & Act
        var result = new TaskResult { ConfidenceScore = score };

        // Assert
        Assert.Equal(score, result.ConfidenceScore);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTaskResultWithNullCollections()
    {
        // Arrange & Act
        var result = new TaskResult
        {
            ToolsUsed = null!,
            Metadata = null!
        };

        // Assert
        Assert.Null(result.ToolsUsed);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void ShouldBeAccepted_WhenUsingTaskResultWithFutureDates()
    {
        // Arrange
        var futureDate = DateTime.UtcNow.AddDays(7);

        // Act
        var result = new TaskResult
        {
            StartedAt = futureDate,
            CompletedAt = futureDate.AddHours(1)
        };

        // Assert
        Assert.True(result.StartedAt > DateTime.UtcNow);
        Assert.True(result.CompletedAt > result.StartedAt);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingTaskResultWithUnicodeContent()
    {
        // Arrange & Act
        var result = new TaskResult
        {
            TaskId = TaskId.Create(),
            AgentId = AgentId.Create(),
            Output = "结果: 成功完成 ✅",
            Error = "错误: 连接失败 ❌",
            ToolsUsed = ["搜索工具 🔍", "翻译器 🌐"],
            Metadata = new Dictionary<string, object>
            {
                { "语言", "中文" },
                { "状态", "完成 ✓" }
            }
        };

        // Assert
        Assert.NotNull(result.TaskId);
        Assert.Contains("✅", result.Output);
        Assert.Contains("❌", result.Error);
        Assert.Contains("🔍", result.ToolsUsed[0]);
        Assert.Contains("中文", result.Metadata["语言"].ToString());
    }

    #endregion

    #region Integration and Scenario Tests

    [Fact]
    public void ShouldSuccessfulAnalysisScenario_WhenUsingTaskResult()
    {
        // Arrange - Simulate a successful data analysis task
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var startTime = DateTime.UtcNow.AddMinutes(-15);
        var endTime = DateTime.UtcNow;
        var duration = endTime - startTime;

        // Act - Create result with factory method then enhance via with expression
        var baseResult = TaskResult.CreateSuccess(
            taskId,
            agentId,
            "Data analysis completed: Found 3 significant patterns",
            duration,
            new
            {
                patterns = new[]
                {
                    new { name = "Seasonal Trend", confidence = 0.92 },
                    new { name = "Weekly Cycle", confidence = 0.87 },
                    new { name = "Growth Pattern", confidence = 0.78 }
                },
                dataPoints = 50000,
                outliers = 127
            });

        var result = baseResult with
        {
            ToolsUsed = s_analysisTools.ToList(),
            ConfidenceScore = 0.89,
            Metadata = new Dictionary<string, object>
            {
                { "algorithm", "Time Series Analysis" },
                { "dataSource", "production_metrics" },
                { "processingTime", duration.TotalSeconds }
            }
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal(4, result.ToolsUsed.Count);
        Assert.Equal(0.89, result.ConfidenceScore);
        Assert.Equal(3, result.Metadata.Count);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ShouldFailureWithRetriesScenario_WhenUsingTaskResult()
    {
        // Arrange - Simulate a task that failed after multiple retries
        var taskId = TaskId.Create();
        var agentId = AgentId.Create();
        var totalDuration = TimeoutStandard; // Including all retries

        // Act
        var baseResult = TaskResult.CreateFailure(
            taskId,
            agentId,
            "API endpoint unreachable after 3 retry attempts",
            totalDuration);

        var result = baseResult with
        {
            RetryCount = 3,
            ToolsUsed = ["HttpClient"],
            Metadata = new Dictionary<string, object>
            {
                { "attempts", new[]
                    {
                        new { attempt = 1, duration = 60, error = "Connection timeout" },
                        new { attempt = 2, duration = 90, error = "Connection timeout" },
                        new { attempt = 3, duration = 120, error = "Service unavailable (503)" },
                        new { attempt = 4, duration = 30, error = "Final attempt - Connection timeout" }
                    }
                },
                { "lastStatusCode", 503 },
                { "endpoint", "https://api.example.com/v1/data" }
            }
        };

        // Assert
        Assert.False(result.Success);
        Assert.Equal(3, result.RetryCount);
        Assert.Contains("3 retry attempts", result.Error);
        Assert.Equal(totalDuration, result.Duration);
        Assert.Single(result.ToolsUsed);
        Assert.Equal(3, result.Metadata.Count);
    }

    [Fact]
    public void ShouldComplexWorkflowScenario_WhenUsingTaskResult()
    {
        // Arrange - Multi-step task with partial success
        var result = new TaskResult
        {
            TaskId = TaskId.Create(),
            AgentId = AgentId.Create(),
            Success = true,
            Output = "Workflow completed with warnings",
            Duration = TimeSpan.FromMinutes(45),
            StartedAt = DateTime.UtcNow.AddMinutes(-45),
            CompletedAt = DateTime.UtcNow,
            RetryCount = 1,
            ConfidenceScore = 0.75,
            StructuredOutput = new
            {
                steps = new[]
                {
                    new { name = "Data Collection", status = "success", duration = 10 },
                    new { name = "Validation", status = "success", duration = 5 },
                    new { name = "Processing", status = "partial", duration = 20 },
                    new { name = "Report Generation", status = "success", duration = 10 }
                },
                overallStatus = "completed_with_warnings",
                processedItems = 9500,
                failedItems = 500,
                successRate = 0.95
            },
            ToolsUsed = s_processingTools.ToList(),
            Metadata = new Dictionary<string, object>
            {
                { "warnings", s_warningMessages },
                { "performance", "degraded" },
                { "recommendations", "Review failed items and optimize processing algorithm" }
            }
        };

        // Assert
        Assert.True(result.Success); // Overall success despite warnings
        Assert.Equal(0.75, result.ConfidenceScore); // Lower confidence due to partial success
        Assert.Equal(5, result.ToolsUsed.Count);
        Assert.Contains("warnings", result.Metadata.Keys);
    }

    #endregion

    #region Factory Method Timestamp Tests

    [Fact]
    public void ShouldBeConsistent_WhenCreatingSuccessTimestamps()
    {
        // Arrange
        var duration = TimeoutExtended;

        // Act
        var result = TaskResult.CreateSuccess(TaskId.Create(), AgentId.Create(), TaskId.Create(), duration);

        // Assert
        Assert.Equal(duration, result.CompletedAt - result.StartedAt);
        Assert.True(result.StartedAt < result.CompletedAt);
        Assert.Equal(DateTimeKind.Utc, result.StartedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, result.CompletedAt.Kind);
    }

    [Fact]
    public void ShouldBeConsistent_WhenCreatingFailureTimestamps()
    {
        // Arrange
        var duration = TimeoutQuick;

        // Act
        var result = TaskResult.CreateFailure(TaskId.Create(), AgentId.Create(), TaskId.Create(), duration);

        // Assert
        Assert.Equal(duration, result.CompletedAt - result.StartedAt);
        Assert.True(result.StartedAt < result.CompletedAt);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingTaskResultToString()
    {
        // Arrange
        var result = new TaskResult
        {
            TaskId = TaskId.Create(),
            Success = true,
            Output = "Test output"
        };

        // Act
        var stringRepresentation = result.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        Assert.Contains("TaskResult", stringRepresentation);
    }

    #endregion
}
