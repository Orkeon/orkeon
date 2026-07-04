using Microsoft.Extensions.Logging;
using Orkeon.Application.Services.Monitoring;
using System.Text.Json;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Application.Tests.Services;

public class PerformanceMetricsCollectorTests
{
    #region Test Doubles

    private class TestLogger : ILogger<PerformanceMetricsCollector>
    {
        public List<string> LoggedMessages { get; } = [];
        public List<Exception> LoggedExceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            LoggedMessages.Add($"[{logLevel}] {message}");
            if (exception != null)
            {
                LoggedExceptions.Add(exception);
            }
        }
    }

    #endregion

    #region Test Helpers

    private static PerformanceMetricsCollector CreateMetricsCollector(TestLogger? logger = null)
    {
        return new PerformanceMetricsCollector(logger);
    }

    private static DateTime GetTestDateTime(int daysAgo = 0, int hoursAgo = 0, int minutesAgo = 0)
    {
        return DateTime.UtcNow.AddDays(-daysAgo).AddHours(-hoursAgo).AddMinutes(-minutesAgo);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldInitialize_WhenConstructingWithoutLogger()
    {
        // Act
        var collector = new PerformanceMetricsCollector();

        // Assert
        Assert.NotNull(collector);
    }

    [Fact]
    public void ShouldInitialize_WhenConstructingWithLogger()
    {
        // Arrange
        var logger = new TestLogger();

        // Act
        var collector = new PerformanceMetricsCollector(logger);

        // Assert
        Assert.NotNull(collector);
    }

    #endregion

    #region RecordTaskExecution Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldRecordMetric_WhenRecordingTaskExecution()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        var agentId = "agent1";
        var taskId = "task1";
        var duration = TimeSpan.FromSeconds(5);
        var success = true;

        // Act
        collector.RecordTaskExecution(agentId, taskId, duration, success);

        // Assert - Generate report to verify metric was recorded
        var report = await collector.GenerateReportAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        Assert.NotNull(report);
        Assert.True(report.AgentMetrics.ContainsKey(agentId));
        Assert.Equal(1, report.AgentMetrics[agentId].TotalTasks);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldAggregateMetrics_WhenRecordingTaskExecutionWithMultipleCallsForSameAgent()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        var agentId = "agent1";

        // Act
        collector.RecordTaskExecution(agentId, "task1", TimeSpan.FromSeconds(3), true);
        collector.RecordTaskExecution(agentId, "task2", TimeSpan.FromSeconds(5), true);
        collector.RecordTaskExecution(agentId, "task3", TimeSpan.FromSeconds(2), false);

        // Assert
        var report = await collector.GenerateReportAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        var agentMetrics = report.AgentMetrics[agentId];
        Assert.Equal(3, agentMetrics.TotalTasks);
        Assert.Equal(2, agentMetrics.SuccessfulTasks);
        Assert.Equal(1, agentMetrics.FailedTasks);
    }

    #endregion

    #region RecordToolUsage Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldRecordMetric_WhenRecordingToolUsage()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        var agentId = "agent1";
        var toolName = "search";
        var duration = TimeSpan.FromMilliseconds(500);

        // Act
        collector.RecordToolUsage(agentId, toolName, duration);

        // Assert
        var report = await collector.GenerateReportAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        Assert.NotNull(report);
        Assert.True(report.ToolMetrics.ContainsKey(toolName));
        Assert.Equal(1, report.ToolMetrics[toolName].TotalCalls);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldAggregateMetrics_WhenRecordingToolUsageWithMultipleCallsForSameTool()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        var toolName = "calculator";

        // Act
        collector.RecordToolUsage("agent1", toolName, TimeSpan.FromMilliseconds(100));
        collector.RecordToolUsage("agent2", toolName, TimeSpan.FromMilliseconds(200));
        collector.RecordToolUsage("agent1", toolName, TimeSpan.FromMilliseconds(300));

        // Assert
        var report = await collector.GenerateReportAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        var toolMetrics = report.ToolMetrics[toolName];
        Assert.Equal(3, toolMetrics.TotalCalls);
        Assert.Equal(TimeSpan.FromMilliseconds(600), toolMetrics.TotalDuration);
    }

    #endregion

    #region RecordMemoryOperation Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldRecordMetric_WhenRecordingMemoryOperation()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        var operation = "Store";
        var duration = TimeSpan.FromMilliseconds(50);
        var itemCount = 10;

        // Act
        collector.RecordMemoryOperation(operation, duration, itemCount);

        // Assert
        var report = await collector.GenerateReportAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        Assert.NotNull(report);
        Assert.Equal(1, report.MemoryMetrics.TotalOperations);
        Assert.Equal(10, report.MemoryMetrics.TotalItems);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldBreakdownByType_WhenRecordingMemoryOperationWithMultipleOperations()
    {
        // Arrange
        var collector = CreateMetricsCollector();

        // Act
        collector.RecordMemoryOperation("Store", TimeSpan.FromMilliseconds(50), 5);
        collector.RecordMemoryOperation("Retrieve", TimeSpan.FromMilliseconds(30), 3);
        collector.RecordMemoryOperation("Store", TimeSpan.FromMilliseconds(40), 7);
        collector.RecordMemoryOperation("Delete", TimeSpan.FromMilliseconds(20), 2);

        // Assert
        var report = await collector.GenerateReportAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        var memoryMetrics = report.MemoryMetrics;
        Assert.Equal(4, memoryMetrics.TotalOperations);
        Assert.Equal(17, memoryMetrics.TotalItems);
        Assert.Equal(3, memoryMetrics.OperationBreakdown.Count);

        var storeMetrics = memoryMetrics.OperationBreakdown["Store"];
        Assert.Equal(2, storeMetrics.Count);
        Assert.Equal(12, storeMetrics.ItemCount);
    }

    #endregion

    #region RecordLlmCall Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldRecordMetric_WhenRecordingLlmCall()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        var provider = "OpenAI";
        var duration = TimeSpan.FromSeconds(2);
        var tokenCount = 150;

        // Act
        collector.RecordLlmCall(provider, duration, tokenCount);

        // Assert
        var report = await collector.GenerateReportAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        Assert.NotNull(report);
        Assert.Equal(1, report.LlmMetrics.TotalCalls);
        Assert.Equal(150, report.LlmMetrics.TotalTokens);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldBreakdownByProvider_WhenRecordingLlmCallWithMultipleProviders()
    {
        // Arrange
        var collector = CreateMetricsCollector();

        // Act
        collector.RecordLlmCall("OpenAI", TimeSpan.FromSeconds(2), 100);
        collector.RecordLlmCall("Anthropic", TimeSpan.FromSeconds(3), 200);
        collector.RecordLlmCall("OpenAI", TimeSpan.FromSeconds(1), 50);

        // Assert
        var report = await collector.GenerateReportAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        var llmMetrics = report.LlmMetrics;
        Assert.Equal(3, llmMetrics.TotalCalls);
        Assert.Equal(350, llmMetrics.TotalTokens);
        Assert.Equal(2, llmMetrics.ProviderBreakdown.Count);

        var openAiMetrics = llmMetrics.ProviderBreakdown["OpenAI"];
        Assert.Equal(2, openAiMetrics.CallCount);
        Assert.Equal(150, openAiMetrics.TokenCount);
    }

    #endregion

    #region GenerateReportAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnEmptyReport_WhenGeneratingReportAsyncWithNoMetrics()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        var from = DateTime.UtcNow.AddHours(-1);
        var to = DateTime.UtcNow;

        // Act
        var report = await collector.GenerateReportAsync(from, to);

        // Assert
        Assert.NotNull(report);
        Assert.Empty(report.AgentMetrics);
        Assert.Empty(report.ToolMetrics);
        Assert.Equal(0, report.MemoryMetrics.TotalOperations);
        Assert.Equal(0, report.LlmMetrics.TotalCalls);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldFilterMetrics_WhenGeneratingReportAsyncWithDateRange()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        var now = DateTime.UtcNow;

        // Record metrics at different times (simulated - in real scenario would need time manipulation)
        collector.RecordTaskExecution("agent1", "task1", TimeSpan.FromSeconds(1), true);
        collector.RecordTaskExecution("agent1", "task2", TimeSpan.FromSeconds(2), true);

        // Act
        var report = await collector.GenerateReportAsync(now.AddMinutes(-5), now.AddMinutes(5));

        // Assert
        Assert.NotNull(report);
        Assert.Single(report.AgentMetrics);
        Assert.Equal(2, report.AgentMetrics["agent1"].TotalTasks);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCalculatePercentiles_WhenGeneratingReportAsync()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        var agentId = "agent1";

        // Add multiple task executions with different durations
        for (int i = 1; i <= 100; i++)
        {
            collector.RecordTaskExecution(agentId, $"task{i}", TimeSpan.FromMilliseconds(i), true);
        }

        // Act
        var report = await collector.GenerateReportAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        // Assert
        var agentMetrics = report.AgentMetrics[agentId];
        Assert.Equal(TimeSpan.FromMilliseconds(1), agentMetrics.MinExecutionTime);
        Assert.Equal(TimeSpan.FromMilliseconds(100), agentMetrics.MaxExecutionTime);
        Assert.Equal(TimeSpan.FromMilliseconds(95), agentMetrics.P95ExecutionTime);
        Assert.Equal(TimeSpan.FromMilliseconds(99), agentMetrics.P99ExecutionTime);
    }

    #endregion

    #region ExportToJsonAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldGenerateValidJson_WhenUsingExportToJsonAsync()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        collector.RecordTaskExecution("agent1", "task1", TimeSpan.FromSeconds(5), true);
        collector.RecordToolUsage("agent1", "search", TimeSpan.FromMilliseconds(500));
        collector.RecordMemoryOperation("Store", TimeSpan.FromMilliseconds(50), 10);
        collector.RecordLlmCall("OpenAI", TimeSpan.FromSeconds(2), 150);

        // Act
        var json = await collector.ExportToJsonAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        // Assert
        Assert.NotNull(json);
        Assert.NotEmpty(json);

        // Verify it's valid JSON
        var report = JsonSerializer.Deserialize<JsonDocument>(json);
        Assert.NotNull(report);

        // Verify structure
        Assert.True(report.RootElement.TryGetProperty("agentMetrics", out _));
        Assert.True(report.RootElement.TryGetProperty("toolMetrics", out _));
        Assert.True(report.RootElement.TryGetProperty("memoryMetrics", out _));
        Assert.True(report.RootElement.TryGetProperty("llmMetrics", out _));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldGenerateValidJson_WhenUsingExportToJsonAsyncWithEmptyReport()
    {
        // Arrange
        var collector = CreateMetricsCollector();

        // Act
        var json = await collector.ExportToJsonAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        // Assert
        Assert.NotNull(json);
        Assert.NotEmpty(json);

        var report = JsonSerializer.Deserialize<JsonDocument>(json);
        Assert.NotNull(report);
    }

    #endregion

    #region ExportToCsvAsync Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldGenerateValidCsv_WhenUsingExportToCsvAsync()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        collector.RecordTaskExecution("agent1", "task1", TimeSpan.FromSeconds(5), true);
        collector.RecordToolUsage("agent1", "search", TimeSpan.FromMilliseconds(500));
        collector.RecordMemoryOperation("Store", TimeSpan.FromMilliseconds(50), 10);
        collector.RecordLlmCall("OpenAI", TimeSpan.FromSeconds(2), 150);

        // Act
        var csv = await collector.ExportToCsvAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        // Assert
        Assert.NotNull(csv);
        Assert.NotEmpty(csv);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 5); // Header + 4 metrics
        Assert.StartsWith("Timestamp,Type,AgentId,Identifier,Duration,Success,ItemCount,TokenCount", lines[0]);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldGenerateHeaderOnly_WhenUsingExportToCsvAsyncWithEmptyReport()
    {
        // Arrange
        var collector = CreateMetricsCollector();

        // Act
        var csv = await collector.ExportToCsvAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        // Assert
        Assert.NotNull(csv);
        Assert.NotEmpty(csv);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines); // Header only
    }

    #endregion

    #region Circular Buffer Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldMaintainCircularBuffer_WhenUsingRecordMetricsUsingBeyondMaxEntries()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        var maxEntries = 10000; // From implementation constant

        // Act - Add more than max entries
        for (int i = 0; i < maxEntries + 100; i++)
        {
            collector.RecordTaskExecution($"agent{i % 10}", $"task{i}", TimeSpan.FromSeconds(1), true);
        }

        // Assert - Should still be able to generate report
        var report = await collector.GenerateReportAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        Assert.NotNull(report);
        // Total recorded should be less than or equal to maxEntries
        var totalTasks = report.AgentMetrics.Values.Sum(m => m.TotalTasks);
        Assert.True(totalTasks <= maxEntries);
    }

    #endregion

    #region Performance Calculation Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldCalculateCorrectly_WhenUsingCalculateMetricsWithVariousDurations()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        var agentId = "agent1";
        var durations = new[] { 100, 200, 300, 400, 500 }; // milliseconds

        // Act
        foreach (var duration in durations)
        {
            collector.RecordTaskExecution(agentId, $"task{duration}", TimeSpan.FromMilliseconds(duration), true);
        }

        // Assert
        var report = await collector.GenerateReportAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        var agentMetrics = report.AgentMetrics[agentId];
        Assert.Equal(TimeSpan.FromMilliseconds(100), agentMetrics.MinExecutionTime);
        Assert.Equal(TimeSpan.FromMilliseconds(500), agentMetrics.MaxExecutionTime);
        Assert.Equal(TimeSpan.FromMilliseconds(300), agentMetrics.AvgExecutionTime);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), agentMetrics.TotalExecutionTime);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldCalculateCorrectly_WhenCalculatingTokensPerSecond()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        var provider = "TestProvider";

        // Act
        collector.RecordLlmCall(provider, TimeSpan.FromSeconds(2), 100);
        collector.RecordLlmCall(provider, TimeSpan.FromSeconds(3), 150);

        // Assert
        var report = await collector.GenerateReportAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        var providerMetrics = report.LlmMetrics.ProviderBreakdown[provider];
        Assert.Equal(50.0, providerMetrics.TokensPerSecond); // 250 tokens / 5 seconds
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenUsingRecordMetricsWithConcurrentCalls()
    {
        // Arrange
        var collector = CreateMetricsCollector();
        var tasks = new List<System.Threading.Tasks.Task>();
        var taskCount = 100;

        // Act
        for (int i = 0; i < taskCount; i++)
        {
            var index = i;
            tasks.Add(System.Threading.Tasks.Task.Run(() =>
            {
                collector.RecordTaskExecution($"agent{index % 5}", $"task{index}", TimeSpan.FromSeconds(1), true);
                collector.RecordToolUsage($"agent{index % 5}", $"tool{index % 3}", TimeSpan.FromMilliseconds(100));
                collector.RecordMemoryOperation($"op{index % 2}", TimeSpan.FromMilliseconds(50), 1);
                collector.RecordLlmCall($"provider{index % 2}", TimeSpan.FromSeconds(1), 100);
            }, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        var report = await collector.GenerateReportAsync(
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(1));

        Assert.NotNull(report);

        // Verify all metrics were recorded
        var totalTasks = report.AgentMetrics.Values.Sum(m => m.TotalTasks);
        Assert.Equal(taskCount, totalTasks);

        var totalToolCalls = report.ToolMetrics.Values.Sum(m => m.TotalCalls);
        Assert.Equal(taskCount, totalToolCalls);

        Assert.Equal(taskCount, report.MemoryMetrics.TotalOperations);
        Assert.Equal(taskCount, report.LlmMetrics.TotalCalls);
    }

    #endregion
}

#pragma warning restore CS0618
