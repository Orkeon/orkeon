using Orkeon.Domain.Flows.ValueObjects;

using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
namespace Orkeon.Domain.Tests.ValueObjects;

public class FlowEventDataTests
{
    private static readonly int[] Int123 = [1, 2, 3];
    private static readonly string[] DataWarnings = ["Large dataset detected", "Memory usage high"];
    #region FlowEventData Tests

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var data = FlowEventData.Empty;

        // Assert
        Assert.NotNull(data);
        Assert.Empty(data.Keys);
        Assert.Equal(0, data.Count);
        Assert.False(data.ContainsKey("anyKey"));
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingWithExistingKey()
    {
        // Arrange
        var prevStepId = FlowStepId.Create();
        var data = FlowEventData.CreateBuilder()
            .AddStepResult(Success)
            .AddPreviousStepId(prevStepId)
            .AddRetryCount(2)
            .Build();

        // Act & Assert
        Assert.Equal(Success, data.Get<string>("step_result"));
        Assert.Equal(prevStepId, data.Get<FlowStepId>("previous_step_id"));
        Assert.Equal(2, data.Get<int>("retry_count"));
    }

    [Fact]
    public void ShouldReturnDefault_WhenGettingWithNonExistentKey()
    {
        // Arrange
        var data = FlowEventData.Empty;

        // Act & Assert
        Assert.Null(data.Get<string>("missing"));
        Assert.Equal(0, data.Get<int>("missing"));
        Assert.False(data.Get<bool>("missing"));
        Assert.Equal(0.0, data.Get<double>("missing"));
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContainsKey()
    {
        // Arrange
        var data = FlowEventData.CreateBuilder()
            .AddMessage("Flow started")
            .Build();

        // Act & Assert
        Assert.True(data.ContainsKey("message"));
        Assert.False(data.ContainsKey("missing_key"));
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenUsingKeys()
    {
        // Arrange
        var data = FlowEventData.CreateBuilder()
            .AddPreviousStepId(FlowStepId.Create())
            .AddNextStepId(FlowStepId.Create())
            .AddMessage("Step completed")
            .Build();

        // Act
        var keys = data.Keys.ToList();

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("previous_step_id", keys);
        Assert.Contains("next_step_id", keys);
        Assert.Contains("message", keys);
    }

    [Fact]
    public void ShouldReturnNumberOfDataItems_WhenCounting()
    {
        // Arrange
        var data = FlowEventData.CreateBuilder()
            .AddStepResult("Done")
            .AddDuration(TimeoutStandard)
            .AddRetryCount(0)
            .AddMessage("No errors")
            .Build();

        // Act & Assert
        Assert.Equal(4, data.Count);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedValue_WhenSetting()
    {
        // Arrange
        var original = FlowEventData.CreateBuilder()
            .AddRetryCount(1)
            .Build();

        // Act
        var updated = original.Set("retry_count", 2);
        var added = updated.Set("new_data", "value");

        // Assert
        Assert.NotSame(original, updated);
        Assert.NotSame(updated, added);

        Assert.Equal(1, original.Get<int>("retry_count"));
        Assert.Equal(2, updated.Get<int>("retry_count"));
        Assert.Equal("value", added.Get<string>("new_data"));

        Assert.Equal(1, original.Count);
        Assert.Equal(1, updated.Count);
        Assert.Equal(2, added.Count);
    }

    [Fact]
    public void ShouldReturnAllData_WhenUsingToDictionary()
    {
        // Arrange
        var prevStepId = FlowStepId.Create();
        var nextStepId = FlowStepId.Create();
        var data = FlowEventData.CreateBuilder()
            .AddStepResult(Completed)
            .AddPreviousStepId(prevStepId)
            .AddNextStepId(nextStepId)
            .AddDuration(TimeoutQuick)
            .AddRetryCount(1)
            .AddMessage("Step processed successfully")
            .Build();

        // Act
        var dict = data.ToDictionary();

        // Assert
        Assert.Equal(6, dict.Count);
        Assert.Equal(Completed, dict["step_result"]);
        Assert.Equal(prevStepId, dict["previous_step_id"]);
        Assert.Equal(nextStepId, dict["next_step_id"]);
        Assert.Equal(30000.0, dict["duration"]); // 30 seconds in milliseconds
        Assert.Equal(1, dict["retry_count"]);
        Assert.Equal("Step processed successfully", dict["message"]);
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddStepResult()
    {
        // Arrange
        var result = new { Status = Success, Data = Int123 };

        // Act
        var data = FlowEventData.CreateBuilder()
            .AddStepResult(result)
            .Build();

        // Assert
        var stored = data.Get<object>("step_result");
        Assert.NotNull(stored);
        Assert.Equal(result, stored);
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddPreviousStepId()
    {
        // Arrange
        var stepId = FlowStepId.Create();

        // Act
        var data = FlowEventData.CreateBuilder()
            .AddPreviousStepId(stepId)
            .Build();

        // Assert
        Assert.Equal(stepId, data.Get<FlowStepId>("previous_step_id"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddNextStepId()
    {
        // Arrange
        var stepId = FlowStepId.Create();

        // Act
        var data = FlowEventData.CreateBuilder()
            .AddNextStepId(stepId)
            .Build();

        // Assert
        Assert.Equal(stepId, data.Get<FlowStepId>("next_step_id"));
    }

    [Fact]
    public void ShouldConvertToMilliseconds_WhenUsingBuilderAddDuration()
    {
        // Arrange
        var duration = TimeSpan.FromMinutes(2.5);

        // Act
        var data = FlowEventData.CreateBuilder()
            .AddDuration(duration)
            .Build();

        // Assert
        Assert.Equal(150000.0, data.Get<double>("duration")); // 2.5 minutes = 150000 ms
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddRetryCount()
    {
        // Act
        var data = FlowEventData.CreateBuilder()
            .AddRetryCount(3)
            .Build();

        // Assert
        Assert.Equal(3, data.Get<int>("retry_count"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddStateSnapshot()
    {
        // Arrange
        var snapshot = new FlowStateSnapshot
        {
            Label = "Checkpoint",
            Timestamp = DateTime.UtcNow,
            State = new Dictionary<string, object>
            {
                { "counter", 42 },
                { "status", "running" }
            }
        };

        // Act
        var data = FlowEventData.CreateBuilder()
            .AddStateSnapshot(snapshot)
            .Build();

        // Assert
        var stored = data.Get<FlowStateSnapshot>("state_snapshot");
        Assert.NotNull(stored);
        Assert.Equal("Checkpoint", stored.Label);
        Assert.Equal(42, stored.State["counter"]);
        Assert.Equal("running", stored.State["status"]);
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddMessage()
    {
        // Act
        var data = FlowEventData.CreateBuilder()
            .AddMessage("Flow execution started at step 1")
            .Build();

        // Assert
        Assert.Equal("Flow execution started at step 1", data.Get<string>("message"));
    }

    [Fact]
    public void ShouldAddCustomData_WhenUsingBuilderAdd()
    {
        // Act
        var data = FlowEventData.CreateBuilder()
            .Add("custom_string", "value")
            .Add("custom_int", 42)
            .Add("custom_bool", true)
            .Add("custom_double", 3.14)
            .Add("custom_object", new { Type = "test", Count = 5 })
            .Build();

        // Assert
        Assert.Equal("value", data.Get<string>("custom_string"));
        Assert.Equal(42, data.Get<int>("custom_int"));
        Assert.True(data.Get<bool>("custom_bool"));
        Assert.Equal(3.14, data.Get<double>("custom_double"));
        Assert.NotNull(data.Get<object>("custom_object"));
    }

    [Fact]
    public void ShouldAddAllData_WhenUsingBuilderChainedCalls()
    {
        // Arrange
        var duration = TimeSpan.FromSeconds(45);
        var prevStepId = FlowStepId.Create();
        var nextStepId = FlowStepId.Create();
        var snapshot = new FlowStateSnapshot
        {
            Label = "Mid-flow",
            Timestamp = DateTime.UtcNow,
            State = new Dictionary<string, object>()
        };

        // Act
        var data = FlowEventData.CreateBuilder()
            .AddStepResult("Partial completion")
            .AddPreviousStepId(prevStepId)
            .AddNextStepId(nextStepId)
            .AddDuration(duration)
            .AddRetryCount(0)
            .AddStateSnapshot(snapshot)
            .AddMessage("Checkpoint reached")
            .Add("custom_metric", 0.95)
            .Build();

        // Assert
        Assert.Equal(8, data.Count);
        Assert.Equal("Partial completion", data.Get<string>("step_result"));
        Assert.Equal(prevStepId, data.Get<FlowStepId>("previous_step_id"));
        Assert.Equal(nextStepId, data.Get<FlowStepId>("next_step_id"));
        Assert.Equal(45000.0, data.Get<double>("duration"));
        Assert.Equal(0, data.Get<int>("retry_count"));
        Assert.NotNull(data.Get<FlowStateSnapshot>("state_snapshot"));
        Assert.Equal("Checkpoint reached", data.Get<string>("message"));
        Assert.Equal(0.95, data.Get<double>("custom_metric"));
    }

    [Fact]
    public void ShouldKeepLastValue_WhenUsingBuilderOverwriteExistingKey()
    {
        // Act
        var data = FlowEventData.CreateBuilder()
            .AddRetryCount(1)
            .AddRetryCount(5)
            .Build();

        // Assert
        Assert.Equal(5, data.Get<int>("retry_count"));
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithNull()
    {
        // Act
        var data = FlowEventData.FromDictionary(null);

        // Assert
        Assert.Same(FlowEventData.Empty, data);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithEmptyDictionary()
    {
        // Act
        var data = FlowEventData.FromDictionary([]);

        // Assert
        Assert.Same(FlowEventData.Empty, data);
    }

    [Fact]
    public void ShouldCreateData_WhenUsingFromDictionaryWithValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "step_result", Success },
            { "previous_step_id", "step-10" },
            { "next_step_id", "step-12" },
            { "retry_count", 2 },
            { "message", "Flow event occurred" }
        };

        // Act
        var data = FlowEventData.FromDictionary(dict);

        // Assert
        Assert.Equal(5, data.Count);
        Assert.Equal(Success, data.Get<string>("step_result"));
        Assert.Equal("step-10", data.Get<string>("previous_step_id"));
        Assert.Equal("step-12", data.Get<string>("next_step_id"));
        Assert.Equal(2, data.Get<int>("retry_count"));
        Assert.Equal("Flow event occurred", data.Get<string>("message"));
    }

    [Fact]
    public void ShouldPreserveValues_WhenUsingRoundTripToDictionaryFromDictionary()
    {
        // Arrange
        var prevStepId = FlowStepId.Create();
        var nextStepId = FlowStepId.Create();
        var original = FlowEventData.CreateBuilder()
            .AddStepResult("Done")
            .AddPreviousStepId(prevStepId)
            .AddNextStepId(nextStepId)
            .AddDuration(TimeSpan.FromSeconds(60))
            .AddRetryCount(1)
            .Add("custom_value", 123.45)
            .Build();

        // Act
        var dict = original.ToDictionary();
        var restored = FlowEventData.FromDictionary(dict);

        // Assert
        Assert.Equal(original.Count, restored.Count);
        Assert.Equal("Done", restored.Get<string>("step_result"));
        Assert.Equal(prevStepId, restored.Get<FlowStepId>("previous_step_id"));
        Assert.Equal(nextStepId, restored.Get<FlowStepId>("next_step_id"));
        Assert.Equal(60000.0, restored.Get<double>("duration"));
        Assert.Equal(1, restored.Get<int>("retry_count"));
        Assert.Equal(123.45, restored.Get<double>("custom_value"));
    }

    #endregion

    #region FlowEventDataValue Tests

    [Fact]
    public void ShouldCreate_WhenUsingFlowEventDataValueFromWithValidValue()
    {
        // Act
        var stringValue = FlowEventDataValue.From("test");
        var intValue = FlowEventDataValue.From(42);
        var boolValue = FlowEventDataValue.From(true);
        var doubleValue = FlowEventDataValue.From(3.14);
        var dateValue = FlowEventDataValue.From(DateTime.UtcNow);
        var complexValue = FlowEventDataValue.From(new FlowStateSnapshot());

        // Assert
        Assert.NotNull(stringValue);
        Assert.NotNull(intValue);
        Assert.NotNull(boolValue);
        Assert.NotNull(doubleValue);
        Assert.NotNull(dateValue);
        Assert.NotNull(complexValue);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingFlowEventDataValueFromWithNull()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => FlowEventDataValue.From(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingFlowEventDataValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = FlowEventDataValue.From("test string");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("test string", result);
    }

    [Fact]
    public void ShouldConvert_WhenUsingFlowEventDataValueGettingValueWithConvertibleType()
    {
        // Arrange
        var intValue = FlowEventDataValue.From(123);

        // Act
        var asString = intValue.GetValue<string>();
        var asDouble = intValue.GetValue<double>();
        var asLong = intValue.GetValue<long>();

        // Assert
        Assert.Equal("123", asString);
        Assert.Equal(123.0, asDouble);
        Assert.Equal(123L, asLong);
    }

    [Fact]
    public void ShouldThrowInvalidCastException_WhenUsingFlowEventDataValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = FlowEventDataValue.From("not a number");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(
            () => value.GetValue<int>());
        Assert.Contains("Cannot convert flow event data value", exception.Message);
        Assert.Contains("String to Int32", exception.Message);
    }

    [Fact]
    public void ShouldReturnOriginalValue_WhenUsingFlowEventDataValueUsingRawValue()
    {
        // Arrange
        var original = new { Name = "Test", Value = 123 };
        var value = FlowEventDataValue.From(original);

        // Act
        var raw = value.RawValue;

        // Assert
        Assert.Same(original, raw);
    }

    [Fact]
    public void ShouldReturnCorrectType_WhenUsingFlowEventDataValueUsingValueType()
    {
        // Arrange
        var stringValue = FlowEventDataValue.From("test");
        var intValue = FlowEventDataValue.From(42);
        var boolValue = FlowEventDataValue.From(true);
        var snapshotValue = FlowEventDataValue.From(new FlowStateSnapshot());

        // Act & Assert
        Assert.Equal(typeof(string), stringValue.ValueType);
        Assert.Equal(typeof(int), intValue.ValueType);
        Assert.Equal(typeof(bool), boolValue.ValueType);
        Assert.Equal(typeof(FlowStateSnapshot), snapshotValue.ValueType);
    }

    #endregion

    #region FlowEventData Equality Tests

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoFlowEventDataWithSameValues()
    {
        // Arrange
        var data1 = FlowEventData.CreateBuilder()
            .AddMessage("Step completed")
            .AddRetryCount(2)
            .Build();

        var data2 = FlowEventData.CreateBuilder()
            .AddMessage("Step completed")
            .AddRetryCount(2)
            .Build();

        // Act & Assert
        Assert.Equal(data1, data2);
        Assert.True(data1.Equals(data2));
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoFlowEventDataWithDifferentValues()
    {
        // Arrange
        var data1 = FlowEventData.CreateBuilder()
            .AddRetryCount(1)
            .Build();

        var data2 = FlowEventData.CreateBuilder()
            .AddRetryCount(5)
            .Build();

        // Act & Assert
        Assert.NotEqual(data1, data2);
        Assert.False(data1.Equals(data2));
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingFlowEventDataWithDifferentCounts()
    {
        // Arrange
        var data1 = FlowEventData.CreateBuilder()
            .AddRetryCount(1)
            .Build();

        var data2 = FlowEventData.CreateBuilder()
            .AddRetryCount(1)
            .AddMessage("Extra")
            .Build();

        // Act & Assert
        Assert.NotEqual(data1, data2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingFlowEventDataWithNull()
    {
        // Arrange
        var data = FlowEventData.CreateBuilder()
            .AddRetryCount(1)
            .Build();

        // Act & Assert
        Assert.False(data.Equals(null));
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingFlowEventDataWithSameReference()
    {
        // Arrange
        var data = FlowEventData.CreateBuilder()
            .AddRetryCount(1)
            .Build();

        // Act & Assert
        Assert.True(data.Equals(data));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingFlowEventDataWithDifferentObjectType()
    {
        // Arrange
        var data = FlowEventData.CreateBuilder()
            .AddRetryCount(1)
            .Build();

        // Act & Assert
        Assert.False(data.Equals("not FlowEventData"));
    }

    [Fact]
    public void ShouldHaveSameHashCode_WhenTwoFlowEventDataAreEqual()
    {
        // Arrange
        var data1 = FlowEventData.CreateBuilder()
            .AddMessage("Hello")
            .AddRetryCount(0)
            .Build();

        var data2 = FlowEventData.CreateBuilder()
            .AddMessage("Hello")
            .AddRetryCount(0)
            .Build();

        // Act & Assert
        Assert.Equal(data1.GetHashCode(), data2.GetHashCode());
    }

    [Fact]
    public void ShouldHaveDifferentHashCode_WhenTwoFlowEventDataAreDifferent()
    {
        // Arrange
        var data1 = FlowEventData.CreateBuilder()
            .AddMessage("msg1")
            .Build();

        var data2 = FlowEventData.CreateBuilder()
            .AddMessage("msg2")
            .Build();

        // Act & Assert
        Assert.NotEqual(data1.GetHashCode(), data2.GetHashCode());
    }

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoEmptyFlowEventData()
    {
        // Arrange
        var data1 = FlowEventData.Empty;
        var data2 = FlowEventData.Empty;

        // Act & Assert
        Assert.Equal(data1, data2);
        Assert.Equal(data1.GetHashCode(), data2.GetHashCode());
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldFlowEventWithAllDataTypes_WhenUsingComplexScenario()
    {
        // Arrange
        var prevStepId = FlowStepId.Create();
        var nextStepId = FlowStepId.Create();
        var snapshot = new FlowStateSnapshot
        {
            Label = "Step 5 Complete",
            Timestamp = DateTime.UtcNow,
            State = new Dictionary<string, object>
            {
                { "processed_items", 150 },
                { "errors", 0 },
                { "status", "healthy" }
            }
        };

        // Act
        var data = FlowEventData.CreateBuilder()
            .AddStepResult(new { Success = true, RecordsProcessed = 150 })
            .AddPreviousStepId(prevStepId)
            .AddNextStepId(nextStepId)
            .AddDuration(TimeSpan.FromMinutes(3.5))
            .AddRetryCount(0)
            .AddStateSnapshot(snapshot)
            .AddMessage("Data validation completed successfully")
            .Add("performance_score", 0.98)
            .Add("warnings", DataWarnings)
            .Add("metadata", new Dictionary<string, string>
            {
                { "version", "1.2.0" },
                { "environment", "production" }
            })
            .Build();

        // Assert
        Assert.Equal(10, data.Count);
        Assert.NotNull(data.Get<object>("step_result"));
        Assert.Equal(prevStepId, data.Get<FlowStepId>("previous_step_id"));
        Assert.Equal(nextStepId, data.Get<FlowStepId>("next_step_id"));
        Assert.Equal(210000.0, data.Get<double>("duration")); // 3.5 minutes
        Assert.Equal(0, data.Get<int>("retry_count"));

        var storedSnapshot = data.Get<FlowStateSnapshot>("state_snapshot");
        Assert.Equal("Step 5 Complete", storedSnapshot!.Label);
        Assert.Equal(150, storedSnapshot!.State!["processed_items"]);

        Assert.Equal(0.98, data.Get<double>("performance_score"));
        Assert.NotNull(data.Get<object>("warnings"));
        Assert.NotNull(data.Get<object>("metadata"));
    }

    [Fact]
    public void ShouldImmutabilityCheck_WhenUsingComplexScenario()
    {
        // Arrange
        var original = FlowEventData.CreateBuilder()
            .AddRetryCount(0)
            .AddMessage("Initial state")
            .Build();

        // Act - Multiple modifications
        var modified1 = original.Set("retry_count", 1);
        var modified2 = modified1.Set("message", "After first retry");
        var modified3 = modified2.Set("error", "Timeout occurred");

        // Assert - Each instance is independent
        Assert.Equal(2, original.Count);
        Assert.Equal(0, original.Get<int>("retry_count"));
        Assert.Equal("Initial state", original.Get<string>("message"));

        Assert.Equal(2, modified1.Count);
        Assert.Equal(1, modified1.Get<int>("retry_count"));
        Assert.Equal("Initial state", modified1.Get<string>("message"));

        Assert.Equal(2, modified2.Count);
        Assert.Equal(1, modified2.Get<int>("retry_count"));
        Assert.Equal("After first retry", modified2.Get<string>("message"));

        Assert.Equal(3, modified3.Count);
        Assert.Equal(1, modified3.Get<int>("retry_count"));
        Assert.Equal("After first retry", modified3.Get<string>("message"));
        Assert.Equal("Timeout occurred", modified3.Get<string>("error"));
    }

    [Fact]
    public void ShouldFlowEventSequence_WhenUsingComplexScenario()
    {
        // Simulate a sequence of flow events
        var nextStep1 = FlowStepId.Create();
        var prevStep2 = FlowStepId.Create();
        var nextStep2 = FlowStepId.Create();
        var prevStep3 = FlowStepId.Create();
        var nextStep3 = FlowStepId.Create();
        var prevStep4 = FlowStepId.Create();
        var nextStep4 = FlowStepId.Create();

        var events = new List<FlowEventData>
        {
            // Event 1: Flow starts
            FlowEventData.CreateBuilder()
            .AddNextStepId(nextStep1)
            .AddMessage("Flow initialized")
            .Add("start_time", DateTime.UtcNow)
            .Build(),

            // Event 2: Step 1 completes
            FlowEventData.CreateBuilder()
            .AddStepResult("Data loaded")
            .AddPreviousStepId(prevStep2)
            .AddNextStepId(nextStep2)
            .AddDuration(TimeSpan.FromSeconds(15))
            .AddRetryCount(0)
            .AddMessage("Step 1 completed")
            .Build(),

            // Event 3: Step 2 fails and retries
            FlowEventData.CreateBuilder()
            .AddStepResult("Error: Connection timeout")
            .AddPreviousStepId(prevStep3)
            .AddNextStepId(nextStep3)
            .AddDuration(TimeoutQuick)
            .AddRetryCount(1)
            .AddMessage("Step 2 failed, retrying")
            .Add("error_code", "TIMEOUT_001")
            .Build(),

            // Event 4: Step 2 succeeds
            FlowEventData.CreateBuilder()
            .AddStepResult("Data processed")
            .AddPreviousStepId(prevStep4)
            .AddNextStepId(nextStep4)
            .AddDuration(TimeSpan.FromSeconds(25))
            .AddRetryCount(2)
            .AddMessage("Step 2 completed after retry")
            .Build()
        };

        // Assert sequence integrity
        Assert.Equal(4, events.Count);

        // Check flow progression
        Assert.False(events[0].ContainsKey("previous_step_id"));
        Assert.Equal(nextStep1, events[0].Get<FlowStepId>("next_step_id"));

        Assert.Equal(prevStep2, events[1].Get<FlowStepId>("previous_step_id"));
        Assert.Equal(nextStep2, events[1].Get<FlowStepId>("next_step_id"));

        Assert.Equal(1, events[2].Get<int>("retry_count"));
        Assert.Equal("TIMEOUT_001", events[2].Get<string>("error_code"));

        Assert.Equal(2, events[3].Get<int>("retry_count"));
        Assert.Contains("completed after retry", events[3].Get<string>("message"));
    }

    #endregion
}
