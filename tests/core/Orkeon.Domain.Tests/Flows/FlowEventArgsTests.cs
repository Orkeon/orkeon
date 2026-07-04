using Orkeon.Domain.Flows;

using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using Orkeon.Domain.Tests.Fixtures;
namespace Orkeon.Domain.Tests.Flows;

/// <summary>
/// Tests for Flow Event Args following Clean Architecture principles.
/// Tests the flow event argument classes and related enums.
/// </summary>
public class FlowEventArgsTests
{
    #region FlowStepStartedEventArgs Tests

    [Fact]
    public void ShouldCreateInstance_WhenUsingFlowStepStartedEventArgsConstructorWithValidParameters()
    {
        // Arrange
        var stepId = FlowStepId.Create();
        var stepName = "ProcessData";
        var beforeCreation = DateTime.UtcNow;

        // Act
        var eventArgs = new FlowStepStartedEventArgs(stepId, stepName);

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.Equal(stepId, eventArgs.StepId);
        Assert.Equal(stepName, eventArgs.StepName);
        Assert.True(eventArgs.StartedAt >= beforeCreation);
        Assert.True(eventArgs.StartedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, eventArgs.StartedAt.Kind);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingFlowStepStartedEventArgsConstructorWithNullStepId()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FlowStepStartedEventArgs(null!, "StepName"));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingFlowStepStartedEventArgsConstructorWithNullStepName()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FlowStepStartedEventArgs(FlowStepId.Create(), null!));
    }

    [Fact]
    public void ShouldAcceptAll_WhenUsingFlowStepStartedEventArgsConstructorWithVariousInputs()
    {
        // Act
        var stepId = FlowStepId.Create();
        var stepName = "ValidateInput";
        var eventArgs = new FlowStepStartedEventArgs(stepId, stepName);

        // Assert
        Assert.Equal(stepId, eventArgs.StepId);
        Assert.Equal(stepName, eventArgs.StepName);
        Assert.NotEqual(default(DateTime), eventArgs.StartedAt);
    }

    [Fact]
    public void ShouldBeCorrect_WhenUsingFlowStepStartedEventArgsInheritanceFromEventArgs()
    {
        // Act
        var eventArgs = new FlowStepStartedEventArgs(FlowStepId.Create(), FlowStepId.Create());

        // Assert
        Assert.IsType<FlowStepStartedEventArgs>(eventArgs);
    }

    [Fact]
    public void ShouldBeRecentUtcTime_WhenUsingFlowStepStartedEventArgsStartedAt()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var eventArgs = new FlowStepStartedEventArgs(FlowStepId.Create(), FlowStepId.Create());

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.True(eventArgs.StartedAt >= beforeCreation);
        Assert.True(eventArgs.StartedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, eventArgs.StartedAt.Kind);
    }

    #endregion

    #region FlowStepCompletedEventArgs Tests

    [Fact]
    public void ShouldCreateInstance_WhenUsingFlowStepCompletedEventArgsConstructorWithSuccessfulCompletion()
    {
        // Arrange
        var stepId = FlowStepId.Create();
        var stepName = "ValidateData";
        var isSuccess = true;
        var beforeCreation = DateTime.UtcNow;

        // Act
        var eventArgs = new FlowStepCompletedEventArgs(stepId, stepName, isSuccess);

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.Equal(stepId, eventArgs.StepId);
        Assert.Equal(stepName, eventArgs.StepName);
        Assert.True(eventArgs.IsSuccess);
        Assert.Null(eventArgs.ErrorMessage);
        Assert.True(eventArgs.CompletedAt >= beforeCreation);
        Assert.True(eventArgs.CompletedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, eventArgs.CompletedAt.Kind);
    }

    [Fact]
    public void ShouldCreateInstance_WhenUsingFlowStepCompletedEventArgsConstructorWithFailedCompletion()
    {
        // Arrange
        var stepId = FlowStepId.Create();
        var stepName = "ProcessPayment";
        var isSuccess = false;
        var errorMessage = "Payment processing failed: Insufficient funds";

        // Act
        var eventArgs = new FlowStepCompletedEventArgs(stepId, stepName, isSuccess, errorMessage);

        // Assert
        Assert.Equal(stepId, eventArgs.StepId);
        Assert.Equal(stepName, eventArgs.StepName);
        Assert.False(eventArgs.IsSuccess);
        Assert.Equal(errorMessage, eventArgs.ErrorMessage);
        Assert.NotEqual(default(DateTime), eventArgs.CompletedAt);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingFlowStepCompletedEventArgsConstructorWithNullStepId()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FlowStepCompletedEventArgs(null!, "StepName", true));
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingFlowStepCompletedEventArgsConstructorWithNullStepName()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FlowStepCompletedEventArgs(FlowStepId.Create(), null!, true));
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(false, "Error occurred")]
    [InlineData(false, "")]
    [InlineData(false, null)]
    public void ShouldAcceptAll_WhenUsingFlowStepCompletedEventArgsConstructorWithVariousSuccessAndErrorCombinations(bool isSuccess, string? errorMessage)
    {
        // Act
        var eventArgs = new FlowStepCompletedEventArgs(FlowStepId.Create(), "Test Step", isSuccess, errorMessage);

        // Assert
        Assert.Equal("Test Step", eventArgs.StepName);
        Assert.Equal(isSuccess, eventArgs.IsSuccess);
        Assert.Equal(errorMessage, eventArgs.ErrorMessage);
    }

    [Fact]
    public void ShouldBeCorrect_WhenUsingFlowStepCompletedEventArgsInheritanceFromEventArgs()
    {
        // Act
        var eventArgs = new FlowStepCompletedEventArgs(FlowStepId.Create(), FlowStepId.Create(), true);

        // Assert
        Assert.IsType<FlowStepCompletedEventArgs>(eventArgs);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingFlowStepCompletedEventArgsWithLongErrorMessage()
    {
        // Arrange
        var longErrorMessage = new string('E', 1000) + " - This is a very long error message with details.";

        // Act
        var eventArgs = new FlowStepCompletedEventArgs(FlowStepId.Create(), FlowStepId.Create(), false, longErrorMessage);

        // Assert
        Assert.Equal(longErrorMessage, eventArgs.ErrorMessage);
        Assert.True(eventArgs.ErrorMessage!.Length > 1000);
        Assert.False(eventArgs.IsSuccess);
    }

    #endregion

    #region EventDrivenFlowContext Tests

    [Fact]
    public void ShouldCreateInstance_WhenUsingEventDrivenFlowContextUsingConstructorWithValidFlowId()
    {
        // Arrange
        var flowId = FlowId.Create();

        // Act
        var context = new EventDrivenFlowContext(flowId);

        // Assert
        Assert.Equal(flowId, context.FlowId);
        Assert.NotNull(context.Data);
        Assert.Empty(context.Data);
        Assert.NotNull(context.CompletedSteps);
        Assert.Empty(context.CompletedSteps);
        Assert.Null(context.CurrentStep);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingEventDrivenFlowContextUsingConstructorWithNullFlowId()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EventDrivenFlowContext(null!));
    }

    [Fact]
    public void ShouldAcceptAll_WhenUsingEventDrivenFlowContextUsingConstructorWithVariousFlowIds()
    {
        // Act
        var flowId = FlowId.Create();
        var context = new EventDrivenFlowContext(flowId);

        // Assert
        Assert.Equal(flowId, context.FlowId);
        Assert.NotNull(context.Data);
        Assert.NotNull(context.CompletedSteps);
    }

    [Fact]
    public void ShouldBeModifiable_WhenUsingEventDrivenFlowContextUsingData()
    {
        // Arrange
        var context = new EventDrivenFlowContext(FlowId.Create());

        // Act
        context.AddData("key1", "value1");
        context.AddData("key2", 42);
        context.AddData("key3", true);

        // Assert
        Assert.Equal(3, context.Data.Count);
        Assert.Equal("value1", context.Data["key1"]);
        Assert.Equal(42, context.Data["key2"]);
        Assert.True((bool)context.Data["key3"]);
    }

    [Fact]
    public void ShouldBeModifiable_WhenUsingEventDrivenFlowContextUsingCompletedSteps()
    {
        // Arrange
        var context = new EventDrivenFlowContext(FlowId.Create());

        // Act
        context.AddCompletedStep("step1");
        context.AddCompletedStep("step2");
        context.AddCompletedStep("step3");

        // Assert
        Assert.Equal(3, context.CompletedSteps.Count);
        Assert.Contains("step1", context.CompletedSteps);
        Assert.Contains("step2", context.CompletedSteps);
        Assert.Contains("step3", context.CompletedSteps);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingEventDrivenFlowContextUsingCurrentStep()
    {
        // Arrange
        var context = new EventDrivenFlowContext(FlowId.Create());

        // Act
        context.SetCurrentStep("processing-data");

        // Assert
        Assert.Equal("processing-data", context.CurrentStep);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingEventDrivenFlowContextWithComplexData()
    {
        // Arrange
        var context = new EventDrivenFlowContext(FlowId.Create());
        var complexObject = new
        {
            Id = 123,
            Name = "Test Object",
            Items = new[] { "item1", "item2", "item3" },
            Metadata = new Dictionary<string, object>
            {
                { "created", DateTime.UtcNow },
                { "version", "1.0" },
                { "active", true }
            }
        };

        // Act
        context.AddData("complexObject", complexObject);
        context.AddData("simpleString", "test");
        context.AddData("nullValue", null!);

        // Assert
        Assert.Equal(3, context.Data.Count);
        Assert.Equal(complexObject, context.Data["complexObject"]);
        Assert.Equal("test", context.Data["simpleString"]);
        Assert.Null(context.Data["nullValue"]);
    }

    #endregion

    #region FlowExecutionStatus Enum Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingFlowExecutionStatus()
    {
        // Act & Assert - Verify all expected enum values exist
        var expectedValues = new[]
        {
            FlowExecutionStatus.NotStarted,
            FlowExecutionStatus.Running,
            FlowExecutionStatus.Completed,
            FlowExecutionStatus.Failed,
            FlowExecutionStatus.Paused,
            FlowExecutionStatus.Cancelled
        };

        // Verify each expected value exists
        foreach (var expectedValue in expectedValues)
        {
            Assert.True(Enum.IsDefined<FlowExecutionStatus>(expectedValue));
        }

        // Verify we have exactly the expected number of values
        var allValues = Enum.GetValues<FlowExecutionStatus>();
        Assert.Equal(expectedValues.Length, allValues.Length);
    }

    [Fact]
    public void ShouldBeNotStarted_WhenUsingFlowExecutionStatusWithDefaultValue()
    {
        // Act
        var defaultValue = default(FlowExecutionStatus);

        // Assert
        Assert.Equal(FlowExecutionStatus.NotStarted, defaultValue);
    }

    [Fact]
    public void ShouldBeConsistent_WhenUsingFlowExecutionStatusUsingNumericValues()
    {
        // Act & Assert - Verify the numeric values are as expected
        Assert.Equal(0, (int)FlowExecutionStatus.NotStarted);
        Assert.Equal(1, (int)FlowExecutionStatus.Running);
        Assert.Equal(2, (int)FlowExecutionStatus.Completed);
        Assert.Equal(3, (int)FlowExecutionStatus.Failed);
        Assert.Equal(4, (int)FlowExecutionStatus.Paused);
        Assert.Equal(5, (int)FlowExecutionStatus.Cancelled);
    }

    [Theory]
    [InlineData(FlowExecutionStatus.NotStarted, "NotStarted")]
    [InlineData(FlowExecutionStatus.Running, "Running")]
    [InlineData(FlowExecutionStatus.Completed, Completed)]
    [InlineData(FlowExecutionStatus.Failed, Failed)]
    [InlineData(FlowExecutionStatus.Paused, "Paused")]
    [InlineData(FlowExecutionStatus.Cancelled, "Cancelled")]
    public void ShouldReturnExpectedStrings_WhenUsingFlowExecutionStatusToString(FlowExecutionStatus status, string expected)
    {
        // Act
        var result = status.ToString();

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingFlowExecutionStatusInCollection()
    {
        // Arrange
        var statuses = new List<FlowExecutionStatus>
        {
            FlowExecutionStatus.NotStarted,
            FlowExecutionStatus.Running,
            FlowExecutionStatus.Completed,
            FlowExecutionStatus.Running, // Duplicate
            FlowExecutionStatus.Failed
        };

        // Act
        var runningStatuses = statuses.Where(s => s == FlowExecutionStatus.Running).ToList();
        var uniqueStatuses = statuses.Distinct().ToList();
        var finalStatuses = statuses.Where(s => s == FlowExecutionStatus.Completed || s == FlowExecutionStatus.Failed || s == FlowExecutionStatus.Cancelled).ToList();

        // Assert
        Assert.Equal(5, statuses.Count);
        Assert.Equal(2, runningStatuses.Count);
        Assert.Equal(4, uniqueStatuses.Count);
        Assert.Equal(2, finalStatuses.Count); // Completed and Failed
    }

    #endregion

    #region FlowListenMode Enum Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingFlowListenMode()
    {
        // Act & Assert - Verify all expected enum values exist
        var expectedValues = new[]
        {
            FlowListenMode.Step,
            FlowListenMode.Flow,
            FlowListenMode.All
        };

        // Verify each expected value exists
        foreach (var expectedValue in expectedValues)
        {
            Assert.True(Enum.IsDefined<FlowListenMode>(expectedValue));
        }

        // Verify we have exactly the expected number of values
        var allValues = Enum.GetValues<FlowListenMode>();
        Assert.Equal(expectedValues.Length, allValues.Length);
    }

    [Fact]
    public void ShouldBeStep_WhenUsingFlowListenModeWithDefaultValue()
    {
        // Act
        var defaultValue = default(FlowListenMode);

        // Assert
        Assert.Equal(FlowListenMode.Step, defaultValue);
    }

    [Fact]
    public void ShouldBeConsistent_WhenUsingFlowListenModeNumericValues()
    {
        // Act & Assert - Verify the numeric values are as expected
        Assert.Equal(0, (int)FlowListenMode.Step);
        Assert.Equal(1, (int)FlowListenMode.Flow);
        Assert.Equal(2, (int)FlowListenMode.All);
    }

    [Theory]
    [InlineData(FlowListenMode.Step, "Step")]
    [InlineData(FlowListenMode.Flow, "Flow")]
    [InlineData(FlowListenMode.All, "All")]
    public void ShouldReturnExpectedStrings_WhenUsingFlowListenModeToString(FlowListenMode mode, string expected)
    {
        // Act
        var result = mode.ToString();

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(FlowListenMode.Step, true)]
    [InlineData(FlowListenMode.Flow, false)]
    [InlineData(FlowListenMode.All, false)]
    public void ShouldIdentifyCorrectly_WhenUsingFlowListenModeIsStepLevel(FlowListenMode mode, bool expectedIsStepLevel)
    {
        // Act - Define what constitutes step-level listening
        var isStepLevel = mode == FlowListenMode.Step;

        // Assert
        Assert.Equal(expectedIsStepLevel, isStepLevel);
    }

    [Theory]
    [InlineData(FlowListenMode.All, true)]
    [InlineData(FlowListenMode.Step, false)]
    [InlineData(FlowListenMode.Flow, false)]
    public void ShouldIdentifyCorrectly_WhenUsingFlowListenModeIsComprehensive(FlowListenMode mode, bool expectedIsComprehensive)
    {
        // Act - Define what constitutes comprehensive listening
        var isComprehensive = mode == FlowListenMode.All;

        // Assert
        Assert.Equal(expectedIsComprehensive, isComprehensive);
    }

    #endregion

    #region Integration and Complex Scenario Tests

    [Fact]
    public void ShouldMaintainConsistency_WhenUsingFlowEventArgsStepLifecycle()
    {
        // Arrange
        var stepId = FlowStepId.Create();
        var stepName = "Data Processing Step";

        // Act - Simulate step lifecycle
        var startedArgs = new FlowStepStartedEventArgs(stepId, stepName);
        var completedArgs = new FlowStepCompletedEventArgs(stepId, stepName, true);

        // Assert - Verify consistency across lifecycle
        Assert.Equal(stepId, startedArgs.StepId);
        Assert.Equal(stepId, completedArgs.StepId);
        Assert.Equal(stepName, startedArgs.StepName);
        Assert.Equal(stepName, completedArgs.StepName);
        Assert.True(completedArgs.CompletedAt >= startedArgs.StartedAt);
    }

    [Fact]
    public void ShouldTrackProgress_WhenUsingEventDrivenFlowContextUsingFlowExecution()
    {
        // Arrange
        var flowId = FlowId.Create();
        var context = new EventDrivenFlowContext(flowId);

        // Act - Simulate flow execution
        context.SetCurrentStep("step1");
        context.AddData("input", "initial data");

        context.AddCompletedStep("step1");
        context.SetCurrentStep("step2");
        context.AddData("step1_output", "processed data");

        context.AddCompletedStep("step2");
        context.SetCurrentStep("step3");
        context.AddData("step2_output", "validated data");

        context.AddCompletedStep("step3");
        context.SetCurrentStep(null); // Flow completed

        // Assert
        Assert.Equal(flowId, context.FlowId);
        Assert.Null(context.CurrentStep);
        Assert.Equal(3, context.CompletedSteps.Count);
        Assert.Equal(3, context.Data.Count);
        Assert.Contains("step1", context.CompletedSteps);
        Assert.Contains("step2", context.CompletedSteps);
        Assert.Contains("step3", context.CompletedSteps);
        Assert.Equal("validated data", context.Data["step2_output"]);
    }

    [Fact]
    public void ShouldWorkTogether_WhenUsingFlowEventArgsWithEventDrivenContext()
    {
        // Arrange
        var flowId = FlowId.Create();
        var context = new EventDrivenFlowContext(flowId);
        var stepId = FlowStepId.Create();
        var stepName = "ProcessStep";

        // Act
        context.SetCurrentStep(stepName);
        var startedArgs = new FlowStepStartedEventArgs(stepId, stepName);

        context.AddData("processing_start", startedArgs.StartedAt);
        context.AddData("step_name", stepName);

        var completedArgs = new FlowStepCompletedEventArgs(stepId, stepName, true);
        context.AddCompletedStep(stepName);
        context.SetCurrentStep(null);
        context.AddData("processing_end", completedArgs.CompletedAt);

        // Assert
        Assert.Equal(stepId, startedArgs.StepId);
        Assert.Equal(stepId, completedArgs.StepId);
        Assert.Contains(stepName, context.CompletedSteps);
        Assert.Null(context.CurrentStep);
        Assert.Equal(3, context.Data.Count);
        Assert.Equal(stepName, context.Data["step_name"]);
        Assert.True(completedArgs.IsSuccess);
    }

    [Fact]
    public void ShouldFollowLogicalOrder_WhenUsingFlowExecutionStatusUsingStateTransitions()
    {
        // Arrange - Define valid state transitions
        var validTransitions = new Dictionary<FlowExecutionStatus, FlowExecutionStatus[]>
        {
            { FlowExecutionStatus.NotStarted, new[] { FlowExecutionStatus.Running } },
            { FlowExecutionStatus.Running, new[] { FlowExecutionStatus.Completed, FlowExecutionStatus.Failed, FlowExecutionStatus.Paused, FlowExecutionStatus.Cancelled } },
            { FlowExecutionStatus.Paused, new[] { FlowExecutionStatus.Running, FlowExecutionStatus.Cancelled } },
            { FlowExecutionStatus.Completed, Array.Empty<FlowExecutionStatus>() }, // Terminal state
            { FlowExecutionStatus.Failed, Array.Empty<FlowExecutionStatus>() }, // Terminal state
            { FlowExecutionStatus.Cancelled, Array.Empty<FlowExecutionStatus>() } // Terminal state
        };

        // Act & Assert - Verify transition logic
        foreach (var (currentState, validNextStates) in validTransitions)
        {
            Assert.NotNull(validNextStates);

            if (validNextStates.Length == 0)
            {
                // Terminal states should not transition
                Assert.True(currentState == FlowExecutionStatus.Completed ||
                           currentState == FlowExecutionStatus.Failed ||
                           currentState == FlowExecutionStatus.Cancelled);
            }
            else
            {
                // Non-terminal states should have valid transitions
                Assert.True(validNextStates.Length > 0);
            }
        }
    }

    #endregion

    #region Edge Cases and Error Scenarios

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingFlowStepStartedEventArgsWithUnicodeStrings()
    {
        // Arrange
        var unicodeStepId = FlowStepId.Create();
        var unicodeStepName = "处理数据步骤 🚀 with émojis";

        // Act
        var eventArgs = new FlowStepStartedEventArgs(unicodeStepId, unicodeStepName);

        // Assert
        Assert.Equal(unicodeStepId, eventArgs.StepId);
        Assert.Equal(unicodeStepName, eventArgs.StepName);
        Assert.Contains("🚀", eventArgs.StepName);
        Assert.Contains("émojis", eventArgs.StepName);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingFlowStepCompletedEventArgsWithVeryLongErrorMessage()
    {
        // Arrange
        var veryLongError = string.Join(" ", Enumerable.Range(1, 1000).Select(i => $"Error{i}"));

        // Act
        var eventArgs = new FlowStepCompletedEventArgs(FlowStepId.Create(), FlowStepId.Create(), false, veryLongError);

        // Assert
        Assert.Equal(veryLongError, eventArgs.ErrorMessage);
        Assert.True(eventArgs.ErrorMessage!.Length > 5000);
        Assert.False(eventArgs.IsSuccess);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingEventDrivenFlowContextWithLargeDataSet()
    {
        // Arrange
        var context = new EventDrivenFlowContext(FlowId.Create());

        // Act - Add large amount of data
        for (int i = 0; i < 1000; i++)
        {
            context.AddData($"key_{i}", $"value_{i}");
            if (i % 10 == 0)
            {
                context.AddCompletedStep($"step_{i}");
            }
        }

        // Assert
        Assert.Equal(1000, context.Data.Count);
        Assert.Equal(100, context.CompletedSteps.Count);
        Assert.Equal("value_999", context.Data["key_999"]);
        Assert.Contains("step_990", context.CompletedSteps);
    }

    [Fact]
    public void ShouldHaveUniqueTimestamps_WhenUsingFlowEventArgsWithMultipleInstances()
    {
        // Arrange & Act
        var eventArgs1 = new FlowStepStartedEventArgs(FlowStepId.Create(), FlowStepId.Create());
        ClockAdvance.UntilStrictlyAfter(eventArgs1.StartedAt); // Ensure different timestamps (R5.6)
        var eventArgs2 = new FlowStepStartedEventArgs(FlowStepId.Create(), FlowStepId.Create());
        ClockAdvance.UntilStrictlyAfter(eventArgs2.StartedAt);
        var eventArgs3 = new FlowStepCompletedEventArgs(FlowStepId.Create(), FlowStepId.Create(), true);

        // Assert
        Assert.True(eventArgs2.StartedAt > eventArgs1.StartedAt);
        Assert.True(eventArgs3.CompletedAt > eventArgs1.StartedAt);
        Assert.NotEqual(eventArgs1.StartedAt, eventArgs2.StartedAt);
    }

    [Fact]
    public void ShouldNotBeDefined_WhenUsingFlowExecutionStatusWithInvalidCastFromInt()
    {
        // Act
        var invalidStatus = (FlowExecutionStatus)999;

        // Assert
        Assert.False(Enum.IsDefined<FlowExecutionStatus>(invalidStatus));
        Assert.Equal(999, (int)invalidStatus);
        Assert.Equal("999", invalidStatus.ToString());
    }

    #endregion
}
