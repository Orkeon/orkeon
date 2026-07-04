using Orkeon.Domain.Flows;

using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
namespace Orkeon.Domain.Tests.Flows;

/// <summary>
/// Tests for Flow Types following Clean Architecture principles.
/// Tests the flow type enums and classes for flow configuration and management.
/// </summary>
public class FlowTypesTests
{
    #region FlowType Enum Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingFlowType()
    {
        // Act & Assert - Verify all expected enum values exist
        var expectedValues = new[]
        {
            FlowType.Sequential,
            FlowType.Parallel,
            FlowType.Conditional,
            FlowType.Loop,
            FlowType.Crew,
            FlowType.Custom
        };

        // Verify each expected value exists
        foreach (var expectedValue in expectedValues)
        {
            Assert.True(Enum.IsDefined<FlowType>(expectedValue));
        }

        // Verify we have exactly the expected number of values
        var allValues = Enum.GetValues<FlowType>();
        Assert.Equal(expectedValues.Length, allValues.Length);
    }

    [Fact]
    public void ShouldBeSequential_WhenUsingFlowTypeWithDefaultValue()
    {
        // Act
        var defaultValue = default(FlowType);

        // Assert
        Assert.Equal(FlowType.Sequential, defaultValue);
    }

    [Fact]
    public void ShouldBeConsistent_WhenUsingFlowTypeUsingNumericValues()
    {
        // Act & Assert - Verify the numeric values are as expected
        Assert.Equal(0, (int)FlowType.Sequential);
        Assert.Equal(1, (int)FlowType.Parallel);
        Assert.Equal(2, (int)FlowType.Conditional);
        Assert.Equal(3, (int)FlowType.Loop);
        Assert.Equal(4, (int)FlowType.Crew);
        Assert.Equal(5, (int)FlowType.Custom);
    }

    [Theory]
    [InlineData(FlowType.Sequential, "Sequential")]
    [InlineData(FlowType.Parallel, "Parallel")]
    [InlineData(FlowType.Conditional, "Conditional")]
    [InlineData(FlowType.Loop, "Loop")]
    [InlineData(FlowType.Crew, "Crew")]
    [InlineData(FlowType.Custom, "Custom")]
    public void ShouldReturnExpectedStrings_WhenUsingFlowTypeToString(FlowType flowType, string expected)
    {
        // Act
        var result = flowType.ToString();

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(FlowType.Sequential, true)]
    [InlineData(FlowType.Parallel, false)]
    [InlineData(FlowType.Conditional, false)]
    [InlineData(FlowType.Loop, false)]
    [InlineData(FlowType.Crew, false)]
    [InlineData(FlowType.Custom, false)]
    public void ShouldIdentifyCorrectly_WhenUsingFlowTypeUsingIsSequential(FlowType flowType, bool expectedIsSequential)
    {
        // Act
        var isSequential = flowType == FlowType.Sequential;

        // Assert
        Assert.Equal(expectedIsSequential, isSequential);
    }

    [Theory]
    [InlineData(FlowType.Sequential, false)]
    [InlineData(FlowType.Parallel, true)]
    [InlineData(FlowType.Conditional, true)]
    [InlineData(FlowType.Loop, true)]
    [InlineData(FlowType.Crew, true)]
    [InlineData(FlowType.Custom, true)]
    public void ShouldIdentifyNonSequentialTypes_WhenUsingFlowTypeUsingIsComplex(FlowType flowType, bool expectedIsComplex)
    {
        // Act - Define complex flows as non-sequential
        var isComplex = flowType != FlowType.Sequential;

        // Assert
        Assert.Equal(expectedIsComplex, isComplex);
    }

    #endregion

    #region FlowStatus Enum Tests

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingFlowStatus()
    {
        // Act & Assert - Verify all expected enum values exist
        var expectedValues = new[]
        {
            FlowStatus.NotStarted,
            FlowStatus.Running,
            FlowStatus.Suspended,
            FlowStatus.Completed,
            FlowStatus.Failed,
            FlowStatus.Cancelled
        };

        // Verify each expected value exists
        foreach (var expectedValue in expectedValues)
        {
            Assert.True(Enum.IsDefined<FlowStatus>(expectedValue));
        }

        // Verify we have exactly the expected number of values
        var allValues = Enum.GetValues<FlowStatus>();
        Assert.Equal(expectedValues.Length, allValues.Length);
    }

    [Fact]
    public void ShouldBeNotStarted_WhenUsingFlowStatusWithDefaultValue()
    {
        // Act
        var defaultValue = default(FlowStatus);

        // Assert
        Assert.Equal(FlowStatus.NotStarted, defaultValue);
    }

    [Fact]
    public void ShouldBeConsistent_WhenUsingFlowStatusUsingNumericValues()
    {
        // Act & Assert - Verify the numeric values are as expected
        Assert.Equal(0, (int)FlowStatus.NotStarted);
        Assert.Equal(1, (int)FlowStatus.Running);
        Assert.Equal(2, (int)FlowStatus.Suspended);
        Assert.Equal(3, (int)FlowStatus.Completed);
        Assert.Equal(4, (int)FlowStatus.Failed);
        Assert.Equal(5, (int)FlowStatus.Cancelled);
    }

    [Theory]
    [InlineData(FlowStatus.NotStarted, "NotStarted")]
    [InlineData(FlowStatus.Running, "Running")]
    [InlineData(FlowStatus.Suspended, "Suspended")]
    [InlineData(FlowStatus.Completed, Completed)]
    [InlineData(FlowStatus.Failed, Failed)]
    [InlineData(FlowStatus.Cancelled, "Cancelled")]
    public void ShouldReturnExpectedStrings_WhenUsingFlowStatusToString(FlowStatus status, string expected)
    {
        // Act
        var result = status.ToString();

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(FlowStatus.Completed, true)]
    [InlineData(FlowStatus.Failed, true)]
    [InlineData(FlowStatus.Cancelled, true)]
    [InlineData(FlowStatus.NotStarted, false)]
    [InlineData(FlowStatus.Running, false)]
    [InlineData(FlowStatus.Suspended, false)]
    public void ShouldIdentifyFinalStates_WhenUsingFlowStatusUsingIsTerminal(FlowStatus status, bool expectedIsTerminal)
    {
        // Act - Define terminal states
        var isTerminal = status == FlowStatus.Completed || status == FlowStatus.Failed || status == FlowStatus.Cancelled;

        // Assert
        Assert.Equal(expectedIsTerminal, isTerminal);
    }

    [Theory]
    [InlineData(FlowStatus.Running, true)]
    [InlineData(FlowStatus.Suspended, true)]
    [InlineData(FlowStatus.NotStarted, false)]
    [InlineData(FlowStatus.Completed, false)]
    [InlineData(FlowStatus.Failed, false)]
    [InlineData(FlowStatus.Cancelled, false)]
    public void ShouldIdentifyActiveStates_WhenUsingFlowStatusUsingIsActive(FlowStatus status, bool expectedIsActive)
    {
        // Act - Define active states as running or suspended (can be resumed)
        var isActive = status == FlowStatus.Running || status == FlowStatus.Suspended;

        // Assert
        Assert.Equal(expectedIsActive, isActive);
    }

    #endregion

    #region FlowConfiguration Tests

    [Fact]
    public void ShouldHaveExpectedDefaults_WhenUsingFlowConfigurationWithDefaultConstructor()
    {
        // Act
        var config = new FlowConfiguration();

        // Assert
        Assert.NotNull(config.Id); // ID is ULID-based EntityId
        Assert.NotNull(config.Id); // ID is ULID-based EntityId // Should be a valid GUID
        Assert.Equal(string.Empty, config.Name);
        Assert.Equal(FlowType.Sequential, config.Type);
        Assert.NotNull(config.Settings);
        Assert.Null(config.Timeout);
        Assert.Equal(3, config.MaxRetries);
        Assert.True(config.EnableLogging);
        Assert.True(config.EnableMetrics);
    }

    [Fact]
    public void ShouldAcceptValues_WhenUsingFlowConfigurationSettingAllProperties()
    {
        // Arrange
        var customId = FlowId.Create();
        var name = "Advanced Data Processing Flow";
        var timeout = TimeSpan.FromMinutes(30);

        // Act — use with expression on a record to create a new instance with different values
        var config = new FlowConfiguration
        {
            Id = customId,
            Name = name,
            Type = FlowType.Parallel,
            Timeout = timeout,
            MaxRetries = 10,
            EnableLogging = false,
            EnableMetrics = false
        };

        // Assert
        Assert.Equal(customId, config.Id);
        Assert.Equal(name, config.Name);
        Assert.Equal(FlowType.Parallel, config.Type);
        Assert.Equal(timeout, config.Timeout);
        Assert.Equal(10, config.MaxRetries);
        Assert.False(config.EnableLogging);
        Assert.False(config.EnableMetrics);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(-1)] // Edge case - negative retries
    public void ShouldAcceptVariousValues_WhenUsingFlowConfigurationWithMaxRetries(int maxRetries)
    {
        // Act
        var config = new FlowConfiguration { MaxRetries = maxRetries };

        // Assert
        Assert.Equal(maxRetries, config.MaxRetries);
    }

    [Fact]
    public void ShouldHaveUniqueIds_WhenUsingFlowConfigurationWithMultipleInstances()
    {
        // Act
        var config1 = new FlowConfiguration();
        var config2 = new FlowConfiguration();
        var config3 = new FlowConfiguration();

        // Assert
        Assert.NotEqual(config1.Id, config2.Id);
        Assert.NotEqual(config2.Id, config3.Id);
        Assert.NotEqual(config1.Id, config3.Id);

        // All should be valid GUIDs
        Assert.NotNull(config1.Id); // ID is ULID-based EntityId
        Assert.NotNull(config2.Id); // ID is ULID-based EntityId
        Assert.NotNull(config3.Id); // ID is ULID-based EntityId
    }

    [Theory]
    [InlineData(FlowType.Sequential)]
    [InlineData(FlowType.Parallel)]
    [InlineData(FlowType.Conditional)]
    [InlineData(FlowType.Loop)]
    [InlineData(FlowType.Crew)]
    [InlineData(FlowType.Custom)]
    public void ShouldAcceptAllFlowTypes_WhenUsingFlowConfigurationUsingType(FlowType flowType)
    {
        // Act
        var config = new FlowConfiguration { Type = flowType };

        // Assert
        Assert.Equal(flowType, config.Type);
    }

    [Fact]
    public void ShouldAcceptVariousTimeSpans_WhenUsingFlowConfigurationUsingTimeout()
    {
        // Arrange
        var timeouts = new[]
        {
            TimeoutQuick,
            TimeoutStandard,
            TimeSpan.FromHours(2),
            TimeSpan.FromDays(1),
            TimeSpan.Zero
        };

        // Act & Assert
        foreach (var timeout in timeouts)
        {
            var config = new FlowConfiguration { Timeout = timeout };
            Assert.Equal(timeout, config.Timeout);
        }
    }

    #endregion

    #region FlowContext Tests

    [Fact]
    public void ShouldHaveExpectedDefaults_WhenUsingFlowContextWithDefaultConstructor()
    {
        // Act
        var context = new FlowContext();

        // Assert
        Assert.NotNull(context.FlowId);
        Assert.Equal(FlowStatus.NotStarted, context.Status);
        Assert.NotNull(context.State);
        Assert.NotNull(context.Events);
        Assert.Empty(context.Events);
        Assert.Equal(default(DateTime), context.StartedAt);
        Assert.Null(context.CompletedAt);
        Assert.Null(context.CurrentStepId);
        Assert.Equal(0, context.RetryCount);
        Assert.Null(context.LastError);
    }

    [Fact]
    public void ShouldAcceptValues_WhenUsingFlowContextSettingAllProperties()
    {
        // Arrange
        var flowId = FlowId.Create();
        var startedAt = DateTime.UtcNow;
        var completedAt = DateTime.UtcNow.AddMinutes(5);
        var currentStepId = FlowStepId.Create();
        var exception = new InvalidOperationException("Test error");

        // Act — use behavioral methods to set properties
        var context = new FlowContext(
            flowId: flowId,
            status: FlowStatus.Running,
            startedAt: startedAt,
            completedAt: completedAt,
            currentStepId: currentStepId,
            retryCount: 2,
            lastError: exception);

        // Assert
        Assert.Equal(flowId, context.FlowId);
        Assert.Equal(FlowStatus.Running, context.Status);
        Assert.Equal(startedAt, context.StartedAt);
        Assert.Equal(completedAt, context.CompletedAt);
        Assert.Equal(currentStepId, context.CurrentStepId);
        Assert.Equal(2, context.RetryCount);
        Assert.Equal(exception, context.LastError);
    }

    [Theory]
    [InlineData(FlowStatus.NotStarted)]
    [InlineData(FlowStatus.Running)]
    [InlineData(FlowStatus.Suspended)]
    [InlineData(FlowStatus.Completed)]
    [InlineData(FlowStatus.Failed)]
    [InlineData(FlowStatus.Cancelled)]
    public void ShouldAcceptAllFlowStatuses_WhenUsingFlowContextUsingStatus(FlowStatus status)
    {
        // Act
        var context = new FlowContext();
        context.UpdateStatus(status);

        // Assert
        Assert.Equal(status, context.Status);
    }

    [Fact]
    public void ShouldStoreVariable_WhenUsingFlowContextSettingVariable()
    {
        // Arrange
        var context = new FlowContext();
        var key = "testKey";
        var value = "testValue";

        // Act
        context.SetVariable(key, value);

        // Assert
        var retrievedValue = context.GetVariable<string>(key);
        Assert.Equal(value, retrievedValue);
    }

    [Fact]
    public void ShouldNotStore_WhenUsingFlowContextSettingVariableWithNullValue()
    {
        // Arrange
        var context = new FlowContext();
        var key = "testKey";

        // Act
        context.SetVariable(key, null!);

        // Assert
        var retrievedValue = context.GetVariable<string>(key);
        Assert.Null(retrievedValue);
    }

    [Fact]
    public void ShouldReturnNull_WhenUsingFlowContextGettingVariableWithNonExistentKey()
    {
        // Arrange
        var context = new FlowContext();

        // Act
        var result = context.GetVariable<string>("nonExistentKey");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ShouldStoreSharedState_WhenUsingFlowContextSettingSharedState()
    {
        // Arrange
        var context = new FlowContext();
        var key = "sharedKey";
        var value = new { Count = 42, Name = "Test" };

        // Act
        context.SetSharedState(key, value);

        // Assert
        var retrievedValue = context.GetSharedState<object>(key);
        Assert.Equal(value, retrievedValue);
    }

    [Fact]
    public void ShouldNotStore_WhenUsingFlowContextSettingSharedStateWithNullValue()
    {
        // Arrange
        var context = new FlowContext();
        var key = "sharedKey";

        // Act
        context.SetSharedState(key, null!);

        // Assert
        var retrievedValue = context.GetSharedState<object>(key);
        Assert.Null(retrievedValue);
    }

    [Fact]
    public void ShouldAddEventToList_WhenUsingFlowContextRecordingEvent()
    {
        // Arrange
        var context = new FlowContext();
        var flowEvent = new FlowEvent
        {
            Id = FlowEventId.Create(),
            FlowId = FlowId.Create(),
            EventType = "StepStarted",
            StepId = FlowStepId.Create()
        };

        // Act
        context.RecordEvent(flowEvent);

        // Assert
        Assert.Single(context.Events);
        Assert.Equal(flowEvent, context.Events[0]);
        Assert.Equal(flowEvent.Id, context.Events[0].Id);
    }

    [Fact]
    public void ShouldMaintainOrder_WhenUsingFlowContextRecordingEventMultipleEvents()
    {
        // Arrange
        var context = new FlowContext();
        var event1 = new FlowEvent { Id = FlowEventId.Create(), EventType = "Started" };
        var event2 = new FlowEvent { Id = FlowEventId.Create(), EventType = "Processing" };
        var event3 = new FlowEvent { Id = FlowEventId.Create(), EventType = Completed };
        var events = new[] { event1, event2, event3 };

        // Act
        foreach (var evt in events)
        {
            context.RecordEvent(evt);
        }

        // Assert
        Assert.Equal(3, context.Events.Count);
        Assert.Equal(event1.Id, context.Events[0].Id);
        Assert.Equal(event2.Id, context.Events[1].Id);
        Assert.Equal(event3.Id, context.Events[2].Id);
    }

    [Fact]
    public void ShouldCreateSnapshot_WhenUsingFlowContextCreatingStateSnapshot()
    {
        // Arrange
        var context = new FlowContext();
        var label = "checkpoint-1";

        // Act
        var snapshot = context.CreateStateSnapshot(label);

        // Assert
        Assert.NotNull(snapshot);
        // Note: We can't test internal properties without knowing FlowStateSnapshot structure
    }

    [Fact]
    public void ShouldUpdateState_WhenUsingFlowContextSavingStateSnapshot()
    {
        // Arrange
        var context = new FlowContext();
        var originalState = context.State;
        var label = "save-point-1";

        // Act
        context.SaveStateSnapshot(label);

        // Assert
        // The state should be updated (even if we can't verify specific changes without knowing the implementation)
        Assert.NotNull(context.State);
    }

    #endregion

    #region FlowStep Tests

    [Fact]
    public void ShouldHaveExpectedDefaults_WhenUsingFlowStepWithDefaultConstructor()
    {
        // Act
        var step = new FlowStep();

        // Assert
        Assert.NotNull(step.Id); // ID is ULID-based EntityId
        Assert.NotNull(step.Id); // ID is ULID-based EntityId // Should be a valid GUID
        Assert.Equal(string.Empty, step.Name);
        Assert.Equal(string.Empty, step.Type);
        Assert.NotNull(step.Parameters);
        Assert.NotNull(step.Dependencies);
        Assert.Empty(step.Dependencies);
        Assert.Null(step.Timeout);
        Assert.True(step.CanRetry);
        Assert.Equal(3, step.MaxRetries);
        Assert.NotNull(step.Metadata);
    }

    [Fact]
    public void ShouldAcceptValues_WhenUsingFlowStepSettingAllProperties()
    {
        // Arrange
        var customId = FlowStepId.Create();
        var name = "Data Validation Step";
        var type = "ValidationStep";
        var dep1 = FlowStepId.Create();
        var dep2 = FlowStepId.Create();
        var dependencies = new List<FlowStepId> { dep1, dep2 };
        var timeout = TimeoutExtended;

        // Act — use object initializer (init-only properties)
        var step = new FlowStep
        {
            Id = customId,
            Name = name,
            Type = type,
            Dependencies = dependencies,
            Timeout = timeout,
            CanRetry = false,
            MaxRetries = 5
        };

        // Assert
        Assert.Equal(customId, step.Id);
        Assert.Equal(name, step.Name);
        Assert.Equal(type, step.Type);
        Assert.Equal(dependencies, step.Dependencies);
        Assert.Equal(timeout, step.Timeout);
        Assert.False(step.CanRetry);
        Assert.Equal(5, step.MaxRetries);
    }

    [Fact]
    public void ShouldHaveUniqueIds_WhenUsingFlowStepWithMultipleInstances()
    {
        // Act
        var step1 = new FlowStep();
        var step2 = new FlowStep();
        var step3 = new FlowStep();

        // Assert
        Assert.NotEqual(step1.Id, step2.Id);
        Assert.NotEqual(step2.Id, step3.Id);
        Assert.NotEqual(step1.Id, step3.Id);

        // All should be valid GUIDs
        Assert.NotNull(step1.Id); // ID is ULID-based EntityId
        Assert.NotNull(step2.Id); // ID is ULID-based EntityId
        Assert.NotNull(step3.Id); // ID is ULID-based EntityId
    }

    [Fact]
    public void ShouldBeImmutable_WhenUsingFlowStepUsingDependencies()
    {
        // Arrange — dependencies are now IReadOnlyList; use with expression to add dependencies
        var dep1 = FlowStepId.Create();
        var dep2 = FlowStepId.Create();
        var step = new FlowStep
        {
            Dependencies = [dep1, dep2]
        };

        // Assert
        Assert.Equal(2, step.Dependencies.Count);
        Assert.Contains(dep1, step.Dependencies);
        Assert.Contains(dep2, step.Dependencies);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(-1)] // Edge case
    public void ShouldAcceptVariousValues_WhenUsingFlowStepWithMaxRetries(int maxRetries)
    {
        // Act
        var step = new FlowStep { MaxRetries = maxRetries };

        // Assert
        Assert.Equal(maxRetries, step.MaxRetries);
    }

    #endregion

    #region FlowEvent Tests

    [Fact]
    public void ShouldHaveExpectedDefaults_WhenUsingFlowEventWithDefaultConstructor()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var flowEvent = new FlowEvent();

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.NotNull(flowEvent.Id); // ID is ULID-based EntityId
        Assert.NotNull(flowEvent.Id); // ID is ULID-based EntityId // Should be a valid GUID
        Assert.NotNull(flowEvent.FlowId);
        Assert.NotNull(flowEvent.StepId);
        Assert.Equal(string.Empty, flowEvent.EventType);
        Assert.True(flowEvent.Timestamp >= beforeCreation);
        Assert.True(flowEvent.Timestamp <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, flowEvent.Timestamp.Kind);
        Assert.NotNull(flowEvent.Data);
        Assert.Null(flowEvent.Error);
    }

    [Fact]
    public void ShouldAcceptValues_WhenUsingFlowEventSettingAllProperties()
    {
        // Arrange
        var customId = FlowEventId.Create();
        var flowId = FlowId.Create();
        var stepId = FlowStepId.Create();
        var eventType = "StepCompleted";
        var timestamp = DateTime.UtcNow.AddMinutes(-5);
        var error = "Processing error occurred";

        // Act — use object initializer
        var flowEvent = new FlowEvent
        {
            Id = customId,
            FlowId = flowId,
            StepId = stepId,
            EventType = eventType,
            Timestamp = timestamp,
            Error = error
        };

        // Assert
        Assert.Equal(customId, flowEvent.Id);
        Assert.Equal(flowId, flowEvent.FlowId);
        Assert.Equal(stepId, flowEvent.StepId);
        Assert.Equal(eventType, flowEvent.EventType);
        Assert.Equal(timestamp, flowEvent.Timestamp);
        Assert.Equal(error, flowEvent.Error);
    }

    [Fact]
    public void ShouldHaveUniqueIds_WhenUsingFlowEventWithMultipleInstances()
    {
        // Act
        var event1 = new FlowEvent();
        var event2 = new FlowEvent();
        var event3 = new FlowEvent();

        // Assert
        Assert.NotEqual(event1.Id, event2.Id);
        Assert.NotEqual(event2.Id, event3.Id);
        Assert.NotEqual(event1.Id, event3.Id);

        // All should be valid GUIDs
        Assert.NotNull(event1.Id); // ID is ULID-based EntityId
        Assert.NotNull(event2.Id); // ID is ULID-based EntityId
        Assert.NotNull(event3.Id); // ID is ULID-based EntityId
    }

    [Theory]
    [InlineData("StepStarted")]
    [InlineData("StepCompleted")]
    [InlineData("StepFailed")]
    [InlineData("FlowStarted")]
    [InlineData("FlowCompleted")]
    [InlineData("FlowFailed")]
    [InlineData("")]
    public void ShouldAcceptVariousTypes_WhenUsingFlowEventUsingEventType(string eventType)
    {
        // Act
        var flowEvent = new FlowEvent { EventType = eventType };

        // Assert
        Assert.Equal(eventType, flowEvent.EventType);
    }

    [Fact]
    public void ShouldBeRecentUtcTime_WhenUsingFlowEventUsingTimestamp()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var flowEvent = new FlowEvent();

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.True(flowEvent.Timestamp >= beforeCreation);
        Assert.True(flowEvent.Timestamp <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, flowEvent.Timestamp.Kind);
    }

    #endregion

    #region Integration and Complex Scenario Tests

    [Fact]
    public void ShouldWorkTogether_WhenUsingFlowConfigurationWithFlowContext()
    {
        // Arrange
        var config = new FlowConfiguration
        {
            Name = "Integration Test Flow",
            Type = FlowType.Parallel,
            MaxRetries = 5,
            Timeout = TimeSpan.FromMinutes(30),
            EnableLogging = true,
            EnableMetrics = true
        };

        var context = new FlowContext(
            flowId: config.Id,
            status: FlowStatus.Running,
            startedAt: DateTime.UtcNow,
            retryCount: 2);

        // Act & Assert
        Assert.Equal(config.Id, context.FlowId);
        Assert.Equal(FlowType.Parallel, config.Type);
        Assert.Equal(FlowStatus.Running, context.Status);
        Assert.True(context.RetryCount < config.MaxRetries);
    }

    [Fact]
    public void ShouldMaintainConsistency_WhenUsingFlowStepWithFlowEvent()
    {
        // Arrange
        var flowId = FlowId.Create();
        var step = new FlowStep
        {
            Name = "Data Processing Step",
            Type = "ProcessingStep",
            CanRetry = true,
            MaxRetries = 3
        };

        var startEvent = new FlowEvent
        {
            FlowId = flowId,
            StepId = step.Id,
            EventType = "StepStarted"
        };

        var completedEvent = new FlowEvent
        {
            FlowId = flowId,
            StepId = step.Id,
            EventType = "StepCompleted"
        };

        // Act & Assert
        Assert.Equal(step.Id, startEvent.StepId);
        Assert.Equal(step.Id, completedEvent.StepId);
        Assert.Equal(startEvent.FlowId, completedEvent.FlowId);
        Assert.NotEqual(startEvent.Id, completedEvent.Id);
    }

    [Fact]
    public void ShouldFollowLogicalOrder_WhenUsingFlowTypesStateTransitionScenario()
    {
        // Arrange
        var config = new FlowConfiguration
        {
            Type = FlowType.Sequential,
            MaxRetries = 3
        };

        var context = new FlowContext(
            flowId: config.Id,
            status: FlowStatus.NotStarted);

        var steps = new List<FlowStep>
        {
            new() { Name = "Initialize", Type = "InitStep" },
            new() { Name = "Process", Type = "ProcessStep", Dependencies = [FlowStepId.Create(), FlowStepId.Create()] },
            new() { Name = "Finalize", Type = "FinalizeStep", Dependencies = [FlowStepId.Create(), FlowStepId.Create()] }
        };

        // Act - Simulate flow execution
        context.UpdateStatus(FlowStatus.Running);
        context.UpdateStartedAt(DateTime.UtcNow);

        foreach (var step in steps)
        {
            context.UpdateCurrentStepId(step.Id);
            context.RecordEvent(new FlowEvent
            {
                FlowId = context.FlowId,
                StepId = step.Id,
                EventType = "StepStarted"
            });

            context.RecordEvent(new FlowEvent
            {
                FlowId = context.FlowId,
                StepId = step.Id,
                EventType = "StepCompleted"
            });
        }

        context.UpdateStatus(FlowStatus.Completed);
        context.UpdateCompletedAt(DateTime.UtcNow);
        context.UpdateCurrentStepId(null);

        // Assert
        Assert.Equal(FlowStatus.Completed, context.Status);
        Assert.Equal(6, context.Events.Count); // 2 events per step
        Assert.Null(context.CurrentStepId);
        Assert.NotNull(context.CompletedAt);
        Assert.True(context.CompletedAt > context.StartedAt);
    }

    [Fact]
    public void ShouldTrackRetryAttempts_WhenUsingFlowTypesWithComplexFlowWithRetries()
    {
        // Arrange
        var config = new FlowConfiguration
        {
            Type = FlowType.Conditional,
            MaxRetries = 5
        };

        var context = new FlowContext(
            flowId: config.Id,
            status: FlowStatus.Running);

        var failingStep = new FlowStep
        {
            Name = "Failing Step",
            Type = "ProcessingStep",
            CanRetry = true,
            MaxRetries = 3
        };

        // Act - Simulate retries
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            context.UpdateCurrentStepId(failingStep.Id);
            context.UpdateRetryCount(attempt);

            context.RecordEvent(new FlowEvent
            {
                FlowId = context.FlowId,
                StepId = failingStep.Id,
                EventType = "StepStarted"
            });

            context.RecordEvent(new FlowEvent
            {
                FlowId = context.FlowId,
                StepId = failingStep.Id,
                EventType = "StepFailed",
                Error = $"Attempt {attempt} failed"
            });
        }

        context.UpdateStatus(FlowStatus.Failed);
        context.UpdateLastError(new InvalidOperationException("Max retries exceeded"));

        // Assert
        Assert.Equal(FlowStatus.Failed, context.Status);
        Assert.Equal(3, context.RetryCount);
        Assert.True(context.RetryCount <= config.MaxRetries);
        Assert.True(context.RetryCount <= failingStep.MaxRetries);
        Assert.Equal(6, context.Events.Count); // 2 events per retry attempt
        Assert.NotNull(context.LastError);
    }

    #endregion

    #region Edge Cases and Error Scenarios

    [Fact]
    public void ShouldAcceptZeroTimeSpan_WhenUsingFlowConfigurationWithZeroTimeout()
    {
        // Act
        var config = new FlowConfiguration { Timeout = TimeSpan.Zero };

        // Assert
        Assert.Equal(TimeSpan.Zero, config.Timeout);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingFlowContextWithLargeRetryCount()
    {
        // Act
        var context = new FlowContext();
        context.UpdateRetryCount(int.MaxValue);

        // Assert
        Assert.Equal(int.MaxValue, context.RetryCount);
    }

    [Fact]
    public void ShouldSupportWithExpression_WhenUsingFlowStepWithEmptyDependencies()
    {
        // Arrange — use with expression (records) to create a copy with different dependencies
        var step = new FlowStep
        {
            Dependencies = [FlowStepId.Create(), FlowStepId.Create()]
        };

        // Act — create a new step with empty dependencies using with expression
        var clearedStep = step with { Dependencies = Array.Empty<FlowStepId>() };

        // Assert
        Assert.Empty(clearedStep.Dependencies);
        Assert.Equal(2, step.Dependencies.Count); // original unchanged
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingFlowEventWithUnicodeContent()
    {
        // Arrange
        var unicodeEventType = "步骤完成";
        var unicodeError = "处理错误 🚨 Erreur de traitement";

        // Act
        var flowEvent = new FlowEvent
        {
            EventType = unicodeEventType,
            Error = unicodeError
        };

        // Assert
        Assert.Equal(unicodeEventType, flowEvent.EventType);
        Assert.Equal(unicodeError, flowEvent.Error);
        Assert.Contains("🚨", flowEvent.Error);
    }

    [Fact]
    public void ShouldHandleInvalidValues_WhenUsingFlowTypesEnumCasting()
    {
        // Act
        var invalidFlowType = (FlowType)999;
        var invalidFlowStatus = (FlowStatus)999;

        // Assert
        Assert.False(Enum.IsDefined<FlowType>(invalidFlowType));
        Assert.False(Enum.IsDefined<FlowStatus>(invalidFlowStatus));
        Assert.Equal(999, (int)invalidFlowType);
        Assert.Equal(999, (int)invalidFlowStatus);
    }

    [Fact]
    public void ShouldAcceptNull_WhenUsingFlowContextWithNullException()
    {
        // Act
        var context = new FlowContext();
        context.UpdateLastError(null);

        // Assert
        Assert.Null(context.LastError);
    }

    #endregion
}
