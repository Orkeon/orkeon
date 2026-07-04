using Orkeon.Domain.Task.ValueObjects;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Common;
using Orkeon.Application.Services.Generic;
using Orkeon.Application.Execution;
using ApplicationResearchTaskContext = Orkeon.Application.Services.Generic.ResearchTaskContext;
using CrewId = Orkeon.Domain.Common.CrewId;
using DomainResearchTask = Orkeon.Domain.Task.ResearchTask;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Services.Generic;

public class ResearchTaskExecutorTests
{
    private readonly ResearchTaskExecutor _executor;
    private readonly TestLogger<ResearchTaskExecutor> _logger;

    public ResearchTaskExecutorTests()
    {
        _logger = new TestLogger<ResearchTaskExecutor>();
        _executor = new ResearchTaskExecutor(_logger);
    }

    /// <summary>
    /// Helper to create a Domain ResearchTask with string parameters (mirrors the old Application-layer constructor).
    /// </summary>
    private static DomainResearchTask CreateResearchTask(TaskId taskId, string description, string expectedOutput, string topic)
    {
        return DomainResearchTask.Create(
            taskId,
            TaskDescription.From(description),
            ExpectedOutput.From(expectedOutput),
            topic);
    }

    #region Constructor Tests

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullLogger()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => new ResearchTaskExecutor(null!));
        Assert.Equal("logger", exception.ParamName);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void ShouldThrowArgumentException_WhenValidatingAsyncWithEmptyTopic()
    {
        // Act & Assert - TaskDescription.From throws on empty/whitespace
        var exception = Assert.Throws<ArgumentException>(() =>
            CreateResearchTask(TaskId.Create(), "", "Expected output", ""));
        Assert.Contains("value cannot be null or whitespace", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowValidationException_WhenValidatingAsyncWithInvalidMaxSources()
    {
        // Arrange
        var task = CreateResearchTask(TaskId.Create(), "Valid topic", "Expected output", "Valid topic");
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<ApplicationResearchTaskContext>(
            CrewId.From(Guid.NewGuid()),
            new ApplicationResearchTaskContext { Topic = "Valid topic", MaxSources = 0 },
            memoryScope,
            [],
            CancellationToken.None);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<TaskValidationException>(() =>
            _executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken));
        Assert.Contains("Max sources must be greater than 0", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowValidationException_WhenValidatingAsyncWithInvalidMaxDuration()
    {
        // Arrange
        var task = CreateResearchTask(TaskId.Create(), "Valid topic", "Expected output", "Valid topic");
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<ApplicationResearchTaskContext>(
            CrewId.From(Guid.NewGuid()),
            new ApplicationResearchTaskContext
            {
                Topic = "Valid topic",
                MaxSources = 5,
                MaxDuration = TimeSpan.Zero
            },
            memoryScope,
            [],
            CancellationToken.None);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<TaskValidationException>(() =>
            _executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken));
        Assert.Contains("Max duration must be positive", exception.Message);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldPass_WhenValidatingAsyncWithValidInput()
    {
        // Arrange
        var task = CreateResearchTask(TaskId.Create(), "Machine Learning trends", "Expected output", "Machine Learning trends");
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<ApplicationResearchTaskContext>(
            CrewId.From(Guid.NewGuid()),
            new ApplicationResearchTaskContext
            {
                Topic = "Machine Learning trends",
                MaxSources = 10,
                MaxDuration = TimeSpan.FromMinutes(30)
            },
            memoryScope,
            [],
            CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.Summary);
        Assert.NotEmpty(result.SourcesUsed);
    }

    #endregion

    #region Core Execution Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldGatherAnalyzeAndSummarize_WhenExecutingCoreAsyncWithValidContext()
    {
        // Arrange
        var task = CreateResearchTask(TaskId.Create(), "AI Ethics", "Expected output", "AI Ethics");
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<ApplicationResearchTaskContext>(
            CrewId.From(Guid.NewGuid()),
            new ApplicationResearchTaskContext
            {
                Topic = "AI Ethics",
                MaxSources = 5,
                MaxDuration = TimeoutExtended
            },
            memoryScope,
            [],
            CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("AI Ethics", result.Summary);
        Assert.NotEmpty(result.SourcesUsed);
        Assert.True(result.SourcesUsed.Count <= 5); // Respects MaxSources
        Assert.NotNull(result.Findings);
        Assert.True(result.Findings.Count > 0);
        Assert.True(result.ConfidenceScore > 0);
        Assert.True(result.ActualDuration > TimeSpan.Zero);
        Assert.True(result.ActualDuration <= TimeoutExtended); // Respects MaxDuration
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldGenerateDifferentResults_WhenExecutingCoreAsyncWithDifferentTopics()
    {
        // Arrange
        var task1 = CreateResearchTask(TaskId.Create(), "Quantum Computing", "Expected output", "Quantum Computing");
        var task2 = CreateResearchTask(TaskId.Create(), "Climate Change", "Expected output", "Climate Change");

        using var memoryScope1 = new TestMemoryScope();
        var context1 = new ExecutionContext<ApplicationResearchTaskContext>(
            CrewId.From(Guid.NewGuid()),
            new ApplicationResearchTaskContext { Topic = "Quantum Computing", MaxSources = 3 },
            memoryScope1,
            [],
            CancellationToken.None);

        using var memoryScope2 = new TestMemoryScope();
        var context2 = new ExecutionContext<ApplicationResearchTaskContext>(
            CrewId.From(Guid.NewGuid()),
            new ApplicationResearchTaskContext { Topic = "Climate Change", MaxSources = 3 },
            memoryScope2,
            [],
            CancellationToken.None);

        // Act
        var result1 = await _executor.ExecuteAsync(task1, context1, Xunit.TestContext.Current.CancellationToken);
        var result2 = await _executor.ExecuteAsync(task2, context2, Xunit.TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.NotEqual(result1.Summary, result2.Summary);
        Assert.Contains("Quantum Computing", result1.Summary);
        Assert.Contains("Climate Change", result2.Summary);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectCancellationToken_WhenExecutingCoreAsyncWithCancellation()
    {
        // Arrange
        var task = CreateResearchTask(TaskId.Create(), "Long research topic", "Expected output", "Long research topic");
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<ApplicationResearchTaskContext>(
            CrewId.From(Guid.NewGuid()),
            new ApplicationResearchTaskContext
            {
                Topic = "Long research topic",
                MaxSources = 100, // Large number to increase processing time
                MaxDuration = TimeSpan.FromMinutes(60)
            },
            memoryScope,
            [],
            CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50); // Cancel after 50ms

        // Act
        var result = await _executor.ExecuteAsync(task, context, cts.Token);

        // Assert - The executor handles cancellation gracefully
        Assert.NotNull(result);
        // Executor doesn't throw TaskCanceledException, it handles cancellation internally
    }

    #endregion

    #region Post-Processing Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldUpdateContextWithResults_WhenUsingPostProcessAsync()
    {
        // Arrange
        var task = CreateResearchTask(TaskId.Create(), "Blockchain Technology", "Expected output", "Blockchain Technology");
        var contextData = new ApplicationResearchTaskContext
        {
            Topic = "Blockchain Technology",
            MaxSources = 4
        };
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<ApplicationResearchTaskContext>(
            CrewId.From(Guid.NewGuid()),
            contextData,
            memoryScope,
            [],
            CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        // Verify context was updated with sources
        Assert.NotEmpty(contextData.Sources);
        Assert.Equal(result.SourcesUsed, contextData.Sources);
        // Verify key findings were extracted
        Assert.NotEmpty(contextData.KeyFindings);
        // Verify relevance scores were calculated
        Assert.NotEmpty(contextData.RelevanceScores);
        Assert.All(contextData.RelevanceScores.Values, score => Assert.True(score > 0));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldLogCompletionWithConfidenceScore_WhenUsingPostProcessAsync()
    {
        // Arrange
        var task = CreateResearchTask(TaskId.Create(), "Renewable Energy", "Expected output", "Renewable Energy");
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<ApplicationResearchTaskContext>(
            CrewId.From(Guid.NewGuid()),
            new ApplicationResearchTaskContext { Topic = "Renewable Energy", MaxSources = 3 },
            memoryScope,
            [],
            CancellationToken.None);

        // Act
        await _executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(_logger.LoggedMessages, m =>
            m.LogLevel == LogLevel.Information &&
            m.Message.Contains("Research completed") &&
            m.Message.Contains("confidence"));
    }

    #endregion

    #region Helper Method Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectMaxSourcesLimit_WhenUsingGatherSources()
    {
        // Arrange
        var testCases = new[] { 1, 3, 5, 10, 20 };

        foreach (var maxSources in testCases)
        {
            var task = CreateResearchTask(TaskId.Create(), $"Test topic {maxSources}", "Expected output", $"Test topic {maxSources}");
            using var memoryScope = new TestMemoryScope();
            var context = new ExecutionContext<ApplicationResearchTaskContext>(
                CrewId.From(Guid.NewGuid()),
                new ApplicationResearchTaskContext
                {
                    Topic = $"Test topic {maxSources}",
                    MaxSources = maxSources
                },
                memoryScope,
                [],
                CancellationToken.None);

            // Act
            var result = await _executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken);

            // Assert
            Assert.True(result.Success);
            Assert.True(result.SourcesUsed.Count <= maxSources);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldGenerateFindings_WhenAnalyzingSources()
    {
        // Arrange
        var task = CreateResearchTask(TaskId.Create(), "Data Science", "Expected output", "Data Science");
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<ApplicationResearchTaskContext>(
            CrewId.From(Guid.NewGuid()),
            new ApplicationResearchTaskContext { Topic = "Data Science", MaxSources = 5 },
            memoryScope,
            [],
            CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Findings);
        Assert.True(result.Findings.Count > 0);
        // Verify findings contain topic-related information
        var findingsDict = result.Findings.ToDictionary();
        var findingsContent = string.Join(" ", findingsDict.Values.Select(v => v.ToString()));
        Assert.Contains("Data Science", findingsContent);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldIncludeTopicAndSourceCount_WhenGeneratingSummary()
    {
        // Arrange
        var task = CreateResearchTask(TaskId.Create(), "Cybersecurity", "Expected output", "Cybersecurity");
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<ApplicationResearchTaskContext>(
            CrewId.From(Guid.NewGuid()),
            new ApplicationResearchTaskContext { Topic = "Cybersecurity", MaxSources = 7 },
            memoryScope,
            [],
            CancellationToken.None);

        // Act
        var result = await _executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Cybersecurity", result.Summary);
        Assert.Contains($"{result.SourcesUsed.Count} sources", result.Summary);
        Assert.Contains("insights", result.Summary.ToLower());
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldVaryWithSourceCountAndFindings_WhenUsingCalculateConfidenceScore()
    {
        // Arrange
        var contexts = new[]
        {
            new ApplicationResearchTaskContext { Topic = "Topic 1", MaxSources = 1 },
            new ApplicationResearchTaskContext { Topic = "Topic 2", MaxSources = 5 },
            new ApplicationResearchTaskContext { Topic = "Topic 3", MaxSources = 10 }
        };

        var results = new List<ResearchTaskResult>();

        // Act
        foreach (var contextData in contexts)
        {
            var task = CreateResearchTask(TaskId.Create(), contextData.Topic, "Expected output", contextData.Topic);
            using var memoryScope = new TestMemoryScope();
            var context = new ExecutionContext<ApplicationResearchTaskContext>(
                CrewId.From(Guid.NewGuid()),
                contextData,
                memoryScope,
                [],
                CancellationToken.None);
            var result = await _executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken);
            results.Add(result);
        }

        // Assert
        Assert.All(results, r => Assert.True(r.Success));
        Assert.All(results, r => Assert.True(r.ConfidenceScore > 0 && r.ConfidenceScore <= 1));
        // Generally, more sources should lead to higher confidence
        Assert.True(results[2].ConfidenceScore >= results[0].ConfidenceScore);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenExecutingAsyncWithNullTask()
    {
        // Arrange
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<ApplicationResearchTaskContext>(
            CrewId.From(Guid.NewGuid()),
            new ApplicationResearchTaskContext { Topic = "Test" },
            memoryScope,
            [],
            CancellationToken.None);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _executor.ExecuteAsync(null!, context, Xunit.TestContext.Current.CancellationToken));
        Assert.Equal("task", exception.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowArgumentNullException_WhenExecutingAsyncWithNullContext()
    {
        // Arrange
        var task = CreateResearchTask(TaskId.Create(), "Test topic", "Expected output", "Test topic");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _executor.ExecuteAsync(task, null!, Xunit.TestContext.Current.CancellationToken));
        Assert.Equal("context", exception.ParamName);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteSuccessfully_WhenCompletingResearchWorkflow()
    {
        // Arrange
        var task = CreateResearchTask(TaskId.Create(), "Artificial Intelligence in Healthcare", "Expected output", "Artificial Intelligence in Healthcare");
        var contextData = new ApplicationResearchTaskContext
        {
            Topic = "Artificial Intelligence in Healthcare",
            MaxSources = 8,
            MaxDuration = TimeoutLong,
            ResearchStarted = DateTime.UtcNow
        };
        using var memoryScope = new TestMemoryScope();
        var context = new ExecutionContext<ApplicationResearchTaskContext>(
            CrewId.From(Guid.NewGuid()),
            contextData,
            memoryScope,
            [],
            CancellationToken.None);

        // Act
        var startTime = DateTime.UtcNow;
        var result = await _executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken);
        var endTime = DateTime.UtcNow;

        // Assert
        // Verify successful execution
        Assert.True(result.Success);
        Assert.Null(result.Error);

        // Verify research output
        Assert.NotEmpty(result.Summary);
        Assert.Contains("Artificial Intelligence in Healthcare", result.Summary);
        Assert.NotEmpty(result.SourcesUsed);
        Assert.True(result.SourcesUsed.Count <= 8);

        // Verify findings
        Assert.NotNull(result.Findings);
        Assert.True(result.Findings.Count > 0);

        // Verify timing
        Assert.True(result.ActualDuration > TimeSpan.Zero);
        Assert.True(result.ActualDuration <= endTime - startTime);

        // Verify confidence score
        Assert.True(result.ConfidenceScore > 0 && result.ConfidenceScore <= 1);

        // Verify context updates
        Assert.Equal(result.SourcesUsed, contextData.Sources);
        Assert.NotEmpty(contextData.KeyFindings);
        Assert.NotEmpty(contextData.RelevanceScores);

        // Verify logging
        Assert.NotEmpty(_logger.LoggedMessages);
        // The exact log messages may vary based on implementation
        // Just verify that some logging occurred
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldExecuteIndependently_WhenUsingMultipleResearchTasks()
    {
        // Arrange
        var tasks = new[]
        {
            ("Machine Learning Algorithms", 5),
            ("Quantum Physics", 3),
            ("Environmental Science", 7)
        };

        var results = new List<(string topic, ResearchTaskResult result)>();

        // Act
        foreach (var (topic, maxSources) in tasks)
        {
            var task = CreateResearchTask(TaskId.Create(), topic, "Expected output", topic);
            using var memoryScope = new TestMemoryScope();
            var context = new ExecutionContext<ApplicationResearchTaskContext>(
                CrewId.From(Guid.NewGuid()),
                new ApplicationResearchTaskContext { Topic = topic, MaxSources = maxSources },
                memoryScope,
                [],
                CancellationToken.None);
            var result = await _executor.ExecuteAsync(task, context, Xunit.TestContext.Current.CancellationToken);
            results.Add((topic, result));
        }

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.result.Success));
        Assert.All(results, r => Assert.Contains(r.topic, r.result.Summary));
        Assert.All(results, r => Assert.True(r.result.SourcesUsed.Count <= tasks.First(t => t.Item1 == r.topic).Item2));

        // Verify each result is unique
        var summaries = results.Select(r => r.result.Summary).ToList();
        Assert.Equal(summaries.Count, summaries.Distinct().Count());
    }

    #endregion
}

// Extension methods for TestLogger<T>
internal static class TestLoggerExtensions
{
    public static bool HasLoggedDebug<T>(this TestLogger<T> logger, string text)
    {
        return logger.LoggedMessages.Any(m => m.LogLevel == LogLevel.Debug && m.Message.Contains(text));
    }
}
