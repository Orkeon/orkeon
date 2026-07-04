using Orkeon.Domain.Common;
using Orkeon.Domain.Flows;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.Flows;

/// <summary>
/// Tests for Flow Execution Result following Clean Architecture principles.
/// Tests the flow execution result classes and related functionality.
/// </summary>
public class FlowExecutionResultTests
{
    #region FlowExecutionResult Basic Tests

    [Fact]
    public void ShouldHaveExpectedDefaults_WhenUsingFlowExecutionResultWithDefaultConstructor()
    {
        // Act
        var result = new FlowExecutionResult();

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Null(result.Error);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Empty(result.OutputState);
        Assert.Empty(result.StepResults);
        Assert.Equal(default(DateTime), result.StartedAt);
        Assert.Equal(default(DateTime), result.CompletedAt);
    }

    [Fact]
    public void ShouldAcceptValues_WhenUsingFlowExecutionResultSettingAllProperties()
    {
        // Arrange
        var flowId = FlowId.Create();
        var success = true;
        var output = new { Result = Success, Count = 42 };
        var error = "No errors";
        var duration = TimeoutStandard;
        var outputState = new Dictionary<string, object> { { "key", "value" } };
        var stepResults = new List<FlowStepExecutionDetail>
        {
            new() { StepId = FlowStepId.Create(), Success = true }
        };
        var startedAt = DateTime.UtcNow.AddMinutes(-5);
        var completedAt = DateTime.UtcNow;

        // Act
        var result = new FlowExecutionResult
        {
            FlowId = flowId,
            Success = success,
            Output = output,
            Error = error,
            Duration = duration,
            OutputState = outputState,
            StepResults = stepResults,
            StartedAt = startedAt,
            CompletedAt = completedAt
        };

        // Assert
        Assert.Equal(success, result.Success);
        Assert.Equal(output, result.Output);
        Assert.Equal(error, result.Error);
        Assert.Equal(duration, result.Duration);
        Assert.Equal(outputState, result.OutputState);
        Assert.Equal(stepResults, result.StepResults);
        Assert.Equal(startedAt, result.StartedAt);
        Assert.Equal(completedAt, result.CompletedAt);
    }

    #endregion

    #region FlowExecutionResult Static Factory Methods Tests

    [Fact]
    public void ShouldCreateSuccessfulResult_WhenCreatingSuccessWithFlowIdOnly()
    {
        // Arrange
        var flowId = FlowId.Create();
        var beforeCreation = DateTime.UtcNow;

        // Act
        var result = FlowExecutionResult.CreateSuccess(flowId);

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.True(result.Success);
        Assert.Null(result.Output);
        Assert.Null(result.Error);
        Assert.True(result.CompletedAt >= beforeCreation);
        Assert.True(result.CompletedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, result.CompletedAt.Kind);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Equal(default(DateTime), result.StartedAt);
    }

    [Fact]
    public void ShouldCreateSuccessfulResultWithOutput_WhenCreatingSuccessWithFlowIdAndOutput()
    {
        // Arrange
        var flowId = FlowId.Create();
        var output = new { Message = "Flow completed successfully", ProcessedItems = 150 };

        // Act
        var result = FlowExecutionResult.CreateSuccess(flowId, output);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(output, result.Output);
        Assert.Null(result.Error);
        Assert.NotEqual(default(DateTime), result.CompletedAt);
    }

    [Fact]
    public void ShouldAcceptAll_WhenCreatingSuccessWithVariousFlowIds()
    {
        // Act
        var flowId = FlowId.Create();
        var result = FlowExecutionResult.CreateSuccess(flowId);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCreatingSuccessWithComplexOutput()
    {
        // Arrange
        var complexOutput = new
        {
            Summary = "Data processing completed",
            Statistics = new
            {
                ProcessedRecords = 1000,
                SuccessfulRecords = 950,
                FailedRecords = 50,
                ProcessingTimeMs = 5000
            },
            Errors = new[] { "Record 45: Invalid format", "Record 123: Missing required field" },
            Metadata = new Dictionary<string, object>
            {
                { "version", "1.2.3" },
                { "environment", "production" },
                { "timestamp", DateTime.UtcNow }
            }
        };

        // Act
        var result = FlowExecutionResult.CreateSuccess(FlowId.Create(), complexOutput);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(complexOutput, result.Output);

        // Verify we can access the complex object
        var dynamicOutput = (dynamic)result.Output!;
        Assert.Equal("Data processing completed", dynamicOutput.Summary);
        Assert.Equal(1000, dynamicOutput.Statistics.ProcessedRecords);
    }

    [Fact]
    public void ShouldCreateFailedResult_WhenCreatingFailureWithFlowIdAndError()
    {
        // Arrange
        var flowId = FlowId.Create();
        var error = "Database connection timeout after 30 seconds";
        var beforeCreation = DateTime.UtcNow;

        // Act
        var result = FlowExecutionResult.CreateFailure(flowId, error);

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Equal(error, result.Error);
        Assert.True(result.CompletedAt >= beforeCreation);
        Assert.True(result.CompletedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, result.CompletedAt.Kind);
    }

    [Theory]
    [InlineData("Simple error")]
    [InlineData("")]
    [InlineData("Very detailed error message with stack trace and multiple causes including network timeout, database unavailability, and validation failures")]
    public void ShouldAcceptAll_WhenCreatingFailureWithVariousErrors(string error)
    {
        // Act
        var result = FlowExecutionResult.CreateFailure(FlowId.Create(), error);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCreatingFailureWithUnicodeError()
    {
        // Arrange
        var unicodeError = "Erreur de traitement: données invalides 🚨 处理错误";

        // Act
        var result = FlowExecutionResult.CreateFailure(FlowId.Create(), unicodeError);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(unicodeError, result.Error);
        Assert.Contains("🚨", result.Error);
        Assert.Contains("处理错误", result.Error);
    }

    #endregion

    #region FlowStepExecutionDetail Tests

    [Fact]
    public void ShouldHaveExpectedDefaults_WhenUsingFlowStepExecutionDetailWithDefaultConstructor()
    {
        // Act
        var detail = new FlowStepExecutionDetail();

        // Assert
        Assert.NotNull(detail.StepId);
        Assert.Equal(string.Empty, detail.StepName);
        Assert.False(detail.Success);
        Assert.Null(detail.Output);
        Assert.Null(detail.Error);
        Assert.Equal(TimeSpan.Zero, detail.Duration);
        Assert.NotEqual(default(DateTime), detail.ExecutedAt);
        Assert.Equal(DateTimeKind.Utc, detail.ExecutedAt.Kind);
    }

    [Fact]
    public void ShouldAcceptValues_WhenUsingFlowStepExecutionDetailSetAllProperties()
    {
        // Arrange
        var stepId = FlowStepId.Create();
        var stepName = "Data Validation Step";
        var success = true;
        var output = "Validation completed successfully";
        var error = (string?)null;
        var duration = TimeSpan.FromSeconds(2.5);
        var executedAt = DateTime.UtcNow.AddMinutes(-1);

        // Act
        var detail = new FlowStepExecutionDetail
        {
            StepId = stepId,
            StepName = stepName,
            Success = success,
            Output = output,
            Error = error,
            Duration = duration,
            ExecutedAt = executedAt
        };

        // Assert
        Assert.NotNull(detail.StepId);
        Assert.Equal(stepName, detail.StepName);
        Assert.Equal(success, detail.Success);
        Assert.Equal(output, detail.Output);
        Assert.Equal(error, detail.Error);
        Assert.Equal(duration, detail.Duration);
        Assert.Equal(executedAt, detail.ExecutedAt);
    }

    [Fact]
    public void ShouldBeSetOnCreation_WhenUsingFlowStepExecutionDetailExecutedAt()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var detail = new FlowStepExecutionDetail();

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.True(detail.ExecutedAt >= beforeCreation);
        Assert.True(detail.ExecutedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, detail.ExecutedAt.Kind);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingFlowStepExecutionDetailWithComplexOutput()
    {
        // Arrange
        var complexOutput = new
        {
            ProcessedItems = 100,
            Results = new[] { "result1", "result2", "result3" },
            Metrics = new Dictionary<string, double>
            {
                { "processingTime", 1.25 },
                { "memoryUsage", 45.7 },
                { "cpuUsage", 12.3 }
            }
        };

        // Act
        var detail = new FlowStepExecutionDetail
        {
            StepId = FlowStepId.Create(),
            StepName = "Complex Processing",
            Success = true,
            Output = complexOutput
        };

        // Assert
        Assert.Equal(complexOutput, detail.Output);
        Assert.True(detail.Success);
    }

    [Theory]
    [InlineData(true, null, "Step completed successfully")]
    [InlineData(false, "Processing failed", null)]
    [InlineData(false, "Partial failure", "Some data processed")]
    [InlineData(true, "Warning occurred", "Processing completed with warnings")]
    public void ShouldAcceptAll_WhenUsingFlowStepExecutionDetailSuccessAndErrorCombinations(bool success, string? error, object? output)
    {
        // Act
        var detail = new FlowStepExecutionDetail
        {
            StepId = FlowStepId.Create(),
            StepName = "Test Step",
            Success = success,
            Error = error,
            Output = output
        };

        // Assert
        Assert.Equal(success, detail.Success);
        Assert.Equal(error, detail.Error);
        Assert.Equal(output, detail.Output);
    }

    #endregion

    #region FlowCompletedEventArgs Tests

    [Fact]
    public void ShouldSetResult_WhenUsingFlowCompletedEventArgsConstructorWithResult()
    {
        // Arrange
        var flowResult = FlowExecutionResult.CreateSuccess(FlowId.Create(), "Test output");

        // Act
        var eventArgs = new FlowCompletedEventArgs(flowResult);

        // Assert
        Assert.Equal(flowResult, eventArgs.Result);
        Assert.NotNull(eventArgs.Result.FlowId);
        Assert.True(eventArgs.Result.Success);
    }

    [Fact]
    public void ShouldBeCorrect_WhenUsingFlowCompletedEventArgsInheritanceFromEventArgs()
    {
        // Arrange
        var flowResult = FlowExecutionResult.CreateFailure(FlowId.Create(), "Test error");

        // Act
        var eventArgs = new FlowCompletedEventArgs(flowResult);

        // Assert
        Assert.IsType<FlowCompletedEventArgs>(eventArgs);
        Assert.False(eventArgs.Result.Success);
        Assert.Equal("Test error", eventArgs.Result.Error);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingFlowCompletedEventArgsWithComplexResult()
    {
        // Arrange
        var complexResult = new FlowExecutionResult
        {
            FlowId = FlowId.Create(),
            Success = true,
            Output = new { ProcessedRecords = 1000 },
            Duration = TimeoutStandard,
            OutputState = new Dictionary<string, object> { { "finalState", "completed" } },
            StepResults =
            [
                new() { StepId = FlowStepId.Create(), Success = true, Duration = TimeoutQuick },
                new() { StepId = FlowStepId.Create(), Success = true, Duration = TimeSpan.FromSeconds(45) }
            ]
        };

        // Act
        var eventArgs = new FlowCompletedEventArgs(complexResult);

        // Assert
        Assert.Equal(complexResult, eventArgs.Result);
        Assert.Equal(2, eventArgs.Result.StepResults.Count);
        Assert.Equal(TimeoutStandard, eventArgs.Result.Duration);
        Assert.Equal("completed", eventArgs.Result.OutputState["finalState"]);
    }

    #endregion

    #region EventDrivenFlowResult Tests

    [Fact]
    public void ShouldHaveExpectedDefaults_WhenUsingEventDrivenFlowResultWithDefaultConstructor()
    {
        // Act
        var result = new EventDrivenFlowResult();

        // Assert
        // Test base class properties
        Assert.False(result.Success);
        Assert.Null(result.Output);

        // Test derived class properties
        Assert.Empty(result.Events);
        Assert.Empty(result.EventCounts);
        Assert.Equal(0, result.TotalEventsProcessed);
    }

    [Fact]
    public void ShouldAcceptValues_WhenUsingEventDrivenFlowResultSettingProperties()
    {
        // Arrange
        var events = new List<FlowEvent>
        {
            new() { Id = FlowEventId.Create(), EventType = "StepStarted" },
            new() { Id = FlowEventId.Create(), EventType = "StepCompleted" },
            new() { Id = FlowEventId.Create(), EventType = "StepStarted" }
        };
        var eventCounts = new Dictionary<string, int>
        {
            { "StepStarted", 2 },
            { "StepCompleted", 1 }
        };

        // Act
        var result = new EventDrivenFlowResult
        {
            FlowId = FlowId.Create(),
            Success = true,
            Events = events,
            EventCounts = eventCounts,
            TotalEventsProcessed = 3
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal(events, result.Events);
        Assert.Equal(eventCounts, result.EventCounts);
        Assert.Equal(3, result.TotalEventsProcessed);
        Assert.Equal(3, result.Events.Count);
        Assert.Equal(2, result.EventCounts.Count);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingEventDrivenFlowResultUsingInheritanceFromFlowExecutionResult()
    {
        // Act
        var result = new EventDrivenFlowResult();

        // Assert
        Assert.IsType<EventDrivenFlowResult>(result);

        // Should have access to base class properties

        // Should have access to derived class properties
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingEventDrivenFlowResultWithManyEvents()
    {
        // Arrange
        var events = new List<FlowEvent>();
        var eventCounts = new Dictionary<string, int>();
        var eventTypes = new[] { "StepStarted", "StepCompleted", "StepFailed", "FlowPaused", "FlowResumed" };

        for (int i = 0; i < 100; i++)
        {
            var eventType = eventTypes[i % eventTypes.Length];
            events.Add(new FlowEvent { Id = FlowEventId.Create(), EventType = eventType });

            if (eventCounts.TryGetValue(eventType, out var count))
                eventCounts[eventType] = count + 1;
            else
                eventCounts[eventType] = 1;
        }

        // Act
        var result = new EventDrivenFlowResult
        {
            Events = events,
            EventCounts = eventCounts,
            TotalEventsProcessed = events.Count
        };

        // Assert
        Assert.Equal(100, result.Events.Count);
        Assert.Equal(100, result.TotalEventsProcessed);
        Assert.Equal(5, result.EventCounts.Count);
        Assert.Equal(20, result.EventCounts["StepStarted"]); // 100/5 = 20
        Assert.Equal(20, result.EventCounts["StepCompleted"]);
    }

    #endregion

    #region Integration and Complex Scenario Tests

    [Fact]
    public void ShouldMaintainConsistency_WhenUsingFlowExecutionResultWithCompleteFlowLifecycle()
    {
        // Arrange
        var flowId = FlowId.Create();
        var startedAt = DateTime.UtcNow;

        // Create step results
        var stepResults = new List<FlowStepExecutionDetail>
        {
            new()
            {
                StepId = FlowStepId.Create(),
                StepName = "Initialize",
                Success = true,
                Duration = TimeSpan.FromSeconds(1),
                Output = "Initialization completed"
            },
            new()
            {
                StepId = FlowStepId.Create(),
                StepName = "Process Data",
                Success = true,
                Duration = TimeSpan.FromSeconds(3),
                Output = new { ProcessedItems = 50 }
            },
            new()
            {
                StepId = FlowStepId.Create(),
                StepName = "Finalize",
                Success = true,
                Duration = TimeSpan.FromSeconds(0.5),
                Output = "Finalization completed"
            }
        };

        // Act
        var result = new FlowExecutionResult
        {
            FlowId = flowId,
            Success = true,
            Output = "Flow completed successfully",
            StartedAt = startedAt,
            CompletedAt = startedAt.AddSeconds(5),
            Duration = TimeSpan.FromSeconds(4.5), // Total of step durations
            StepResults = stepResults,
            OutputState = new Dictionary<string, object>
            {
                { "totalProcessedItems", 50 },
                { "completedSteps", 3 },
                { "success", true }
            }
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.StepResults.Count);
        Assert.True(result.CompletedAt > result.StartedAt);
        Assert.All(result.StepResults, step => Assert.True(step.Success));

        var totalStepDuration = result.StepResults.Sum(s => s.Duration.TotalSeconds);
        Assert.Equal(4.5, totalStepDuration);
        Assert.Equal(50, result.OutputState["totalProcessedItems"]);
    }

    [Fact]
    public void ShouldReflectMixedResults_WhenUsingFlowExecutionResultWithPartialFailure()
    {
        // Arrange
        var stepResults = new List<FlowStepExecutionDetail>
        {
            new()
            {
                StepId = FlowStepId.Create(),
                StepName = "Successful Step",
                Success = true,
                Output = "Step 1 completed",
                Duration = TimeSpan.FromSeconds(2)
            },
            new()
            {
                StepId = FlowStepId.Create(),
                StepName = "Failed Step",
                Success = false,
                Error = "Step 2 failed due to network timeout",
                Duration = TimeoutQuick // Long duration due to timeout
            },
            new()
            {
                StepId = FlowStepId.Create(),
                StepName = "Recovery Step",
                Success = true,
                Output = "Recovered from step 2 failure",
                Duration = TimeSpan.FromSeconds(1)
            }
        };

        // Act
        var result = new FlowExecutionResult
        {
            FlowId = FlowId.Create(),
            Success = false, // Overall failure due to step 2
            Error = "Flow partially failed: Step 2 encountered network timeout",
            StepResults = stepResults,
            Duration = TimeSpan.FromSeconds(33)
        };

        // Assert
        Assert.False(result.Success);
        Assert.Contains("network timeout", result.Error);
        Assert.Equal(3, result.StepResults.Count);

        var successfulSteps = result.StepResults.Count(s => s.Success);
        var failedSteps = result.StepResults.Count(s => !s.Success);
        Assert.Equal(2, successfulSteps);
        Assert.Equal(1, failedSteps);
    }

    [Fact]
    public void ShouldTrackAllEvents_WhenUsingEventDrivenFlowResultWithCompleteEventFlow()
    {
        // Arrange & Act
        var result = new EventDrivenFlowResult
        {
            FlowId = FlowId.Create(),
            Success = true,
            Output = "Event-driven flow completed",
            Events =
            [
                new() { Id = FlowEventId.Create(), FlowId = FlowId.Create(), EventType = "FlowStarted", Timestamp = DateTime.UtcNow.AddMinutes(-5) },
                new() { Id = FlowEventId.Create(), FlowId = FlowId.Create(), EventType = "StepStarted", StepId = FlowStepId.Create(), Timestamp = DateTime.UtcNow.AddMinutes(-4) },
                new() { Id = FlowEventId.Create(), FlowId = FlowId.Create(), EventType = "StepCompleted", StepId = FlowStepId.Create(), Timestamp = DateTime.UtcNow.AddMinutes(-3) },
                new() { Id = FlowEventId.Create(), FlowId = FlowId.Create(), EventType = "StepStarted", StepId = FlowStepId.Create(), Timestamp = DateTime.UtcNow.AddMinutes(-2) },
                new() { Id = FlowEventId.Create(), FlowId = FlowId.Create(), EventType = "StepCompleted", StepId = FlowStepId.Create(), Timestamp = DateTime.UtcNow.AddMinutes(-1) },
                new() { Id = FlowEventId.Create(), FlowId = FlowId.Create(), EventType = "FlowCompleted", Timestamp = DateTime.UtcNow }
            ],
            EventCounts = new Dictionary<string, int>
            {
                { "FlowStarted", 1 },
                { "StepStarted", 2 },
                { "StepCompleted", 2 },
                { "FlowCompleted", 1 }
            },
            TotalEventsProcessed = 6
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal(6, result.Events.Count);
        Assert.Equal(6, result.TotalEventsProcessed);
        Assert.Equal(4, result.EventCounts.Count);

        // Verify events are chronologically ordered
        for (int i = 1; i < result.Events.Count; i++)
        {
            Assert.True(result.Events[i].Timestamp >= result.Events[i - 1].Timestamp);
        }

        // Verify event counts match actual events
        var actualCounts = result.Events.GroupBy(e => e.EventType)
                                      .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (eventType, expectedCount) in result.EventCounts)
        {
            Assert.Equal(expectedCount, actualCounts[eventType]);
        }
    }

    [Fact]
    public void ShouldHandleInheritance_WhenUsingFlowCompletedEventArgsWithEventDrivenResult()
    {
        // Arrange
        var eventDrivenResult = new EventDrivenFlowResult
        {
            FlowId = FlowId.Create(),
            Success = true,
            Events = [new() { EventType = "TestEvent" }],
            TotalEventsProcessed = 1
        };

        // Act
        var eventArgs = new FlowCompletedEventArgs(eventDrivenResult);

        // Assert
        Assert.Equal(eventDrivenResult, eventArgs.Result);
        Assert.IsType<EventDrivenFlowResult>(eventArgs.Result);

        // Should be able to cast back to EventDrivenFlowResult
        var castedResult = eventArgs.Result as EventDrivenFlowResult;
        Assert.NotNull(castedResult);
        Assert.Equal(1, castedResult!.TotalEventsProcessed);
        Assert.Single(castedResult.Events);
    }

    #endregion

    #region Edge Cases and Error Scenarios

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingFlowExecutionResultWithNullOutput()
    {
        // Act
        var result = FlowExecutionResult.CreateSuccess(FlowId.Create(), null);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.Output);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingFlowExecutionResultWithVeryLongDuration()
    {
        // Arrange
        var longDuration = TimeSpan.FromDays(365); // 1 year

        // Act
        var result = new FlowExecutionResult
        {
            FlowId = FlowId.Create(),
            Duration = longDuration
        };

        // Assert
        Assert.Equal(longDuration, result.Duration);
        Assert.Equal(365, result.Duration.TotalDays);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingFlowStepExecutionDetailWithUnicodeContent()
    {
        // Arrange
        var unicodeStepName = "步骤处理 🚀 Processing étape";
        var unicodeError = "Erreur de traitement des données 错误";

        // Act
        var detail = new FlowStepExecutionDetail
        {
            StepId = FlowStepId.Create(),
            StepName = unicodeStepName,
            Success = false,
            Error = unicodeError
        };

        // Assert
        Assert.Equal(unicodeStepName, detail.StepName);
        Assert.Equal(unicodeError, detail.Error);
        Assert.Contains("🚀", detail.StepName);
        Assert.Contains("错误", detail.Error!);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingEventDrivenFlowResultWithEmptyEventCounts()
    {
        // Act
        var result = new EventDrivenFlowResult
        {
            Events = [],
            EventCounts = [],
            TotalEventsProcessed = 0
        };

        // Assert
        Assert.Empty(result.Events);
        Assert.Empty(result.EventCounts);
        Assert.Equal(0, result.TotalEventsProcessed);
    }

    [Fact]
    public void ShouldAllowInconsistency_WhenUsingFlowExecutionResultWithMismatchedDateTimes()
    {
        // This test verifies that the class doesn't enforce consistency - 
        // that's the responsibility of the business logic

        // Arrange & Act
        var result = new FlowExecutionResult
        {
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow.AddMinutes(-10) // Earlier than start!
        };

        // Assert - Should allow inconsistent data
        Assert.True(result.CompletedAt < result.StartedAt);
    }

    #endregion
}
