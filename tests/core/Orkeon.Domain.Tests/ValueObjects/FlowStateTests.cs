using Orkeon.Domain.Flows.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestStatusConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class FlowStateTests
{
    #region FlowState Tests

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var state = FlowState.Empty;

        // Assert
        Assert.NotNull(state);
        Assert.Empty(state.Keys);
        Assert.Equal(0, state.Count);
        Assert.False(state.ContainsKey("anyKey"));
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingWithExistingKey()
    {
        // Arrange
        var state = FlowState.CreateBuilder()
            .AddCurrentStep("step-3")
            .AddIterationCount(5)
            .AddUserInput("user data")
            .Build();

        // Act & Assert
        Assert.Equal("step-3", state.Get<string>("current_step"));
        Assert.Equal(5, state.Get<int>("iteration_count"));
        Assert.Equal("user data", state.Get<string>("user_input"));
    }

    [Fact]
    public void ShouldReturnDefault_WhenGettingWithNonExistentKey()
    {
        // Arrange
        var state = FlowState.Empty;

        // Act & Assert
        Assert.Null(state.Get<string>("missing"));
        Assert.Equal(0, state.Get<int>("missing"));
        Assert.False(state.Get<bool>("missing"));
        Assert.Equal(0.0, state.Get<double>("missing"));
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingRequiredWithExistingKey()
    {
        // Arrange
        var state = FlowState.CreateBuilder()
            .AddCurrentStep("validation")
            .Build();

        // Act
        var result = state.GetRequired<string>("current_step");

        // Assert
        Assert.Equal("validation", result);
    }

    [Fact]
    public void ShouldThrowKeyNotFoundException_WhenGettingRequiredWithNonExistentKey()
    {
        // Arrange
        var state = FlowState.Empty;

        // Act & Assert
        var exception = Assert.Throws<KeyNotFoundException>(
            () => state.GetRequired<string>("missing"));
        Assert.Contains("Required flow state key 'missing' not found", exception.Message);
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContainsKey()
    {
        // Arrange
        var state = FlowState.CreateBuilder()
            .AddOutput("result data")
            .Build();

        // Act & Assert
        Assert.True(state.ContainsKey("output"));
        Assert.False(state.ContainsKey("missing_key"));
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenUsingKeys()
    {
        // Arrange
        var state = FlowState.CreateBuilder()
            .AddCurrentStep("processing")
            .AddStartTime(DateTime.UtcNow)
            .AddIterationCount(3)
            .Build();

        // Act
        var keys = state.Keys.ToList();

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("current_step", keys);
        Assert.Contains("start_time", keys);
        Assert.Contains("iteration_count", keys);
    }

    [Fact]
    public void ShouldReturnNumberOfItems_WhenCounting()
    {
        // Arrange
        var state = FlowState.CreateBuilder()
            .AddCurrentStep("step1")
            .AddCompletedSteps(new List<string> { "init", "validate" })
            .AddStartTime(DateTime.UtcNow)
            .AddIterationCount(2)
            .Build();

        // Act & Assert
        Assert.Equal(4, state.Count);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedValue_WhenSetting()
    {
        // Arrange
        var original = FlowState.CreateBuilder()
            .AddIterationCount(1)
            .Build();

        // Act
        var updated = original.Set("iteration_count", 2);
        var added = updated.Set("new_key", "new value");

        // Assert
        Assert.NotSame(original, updated);
        Assert.NotSame(updated, added);

        Assert.Equal(1, original.Get<int>("iteration_count"));
        Assert.Equal(2, updated.Get<int>("iteration_count"));
        Assert.Equal("new value", added.Get<string>("new_key"));

        Assert.Equal(1, original.Count);
        Assert.Equal(1, updated.Count);
        Assert.Equal(2, added.Count);
    }

    [Fact]
    public void ShouldUpdateMultipleValues_WhenSettingMultiple()
    {
        // Arrange
        var original = FlowState.CreateBuilder()
            .AddCurrentStep("step1")
            .Build();

        var updates = new[]
        {
            new KeyValuePair<string, object>("current_step", "step2"),
            new KeyValuePair<string, object>("progress", 0.5),
            new KeyValuePair<string, object>("status", "running")
        };

        // Act
        var updated = original.SetMultiple(updates);

        // Assert
        Assert.Equal(3, updated.Count);
        Assert.Equal("step2", updated.Get<string>("current_step"));
        Assert.Equal(0.5, updated.Get<double>("progress"));
        Assert.Equal("running", updated.Get<string>("status"));
    }

    [Fact]
    public void ShouldCreateNewInstanceWithoutKey_WhenRemoving()
    {
        // Arrange
        var original = FlowState.CreateBuilder()
            .AddCurrentStep("step1")
            .AddIterationCount(5)
            .AddError("Some error")
            .Build();

        // Act
        var withoutError = original.Remove("error");

        // Assert
        Assert.Equal(3, original.Count);
        Assert.Equal(2, withoutError.Count);
        Assert.True(original.ContainsKey("error"));
        Assert.False(withoutError.ContainsKey("error"));
        Assert.Equal("step1", withoutError.Get<string>("current_step"));
        Assert.Equal(5, withoutError.Get<int>("iteration_count"));
    }

    [Fact]
    public void ShouldReturnAllState_WhenUsingToDictionary()
    {
        // Arrange
        var startTime = DateTime.UtcNow;
        var state = FlowState.CreateBuilder()
            .AddCurrentStep("validation")
            .AddCompletedSteps(["init", "load"])
            .AddStartTime(startTime)
            .AddIterationCount(3)
            .AddOutput(new { result = "success", count = 42 })
            .Build();

        // Act
        var dict = state.ToDictionary();

        // Assert
        Assert.Equal(5, dict.Count);
        Assert.Equal("validation", dict["current_step"]);
        Assert.Equal(3, dict["iteration_count"]);
        Assert.Equal(startTime, dict["start_time"]);
        Assert.NotNull(dict["completed_steps"]);
        Assert.NotNull(dict["output"]);
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddCurrentStep()
    {
        // Act
        var state = FlowState.CreateBuilder()
            .AddCurrentStep("data-processing")
            .Build();

        // Assert
        Assert.Equal("data-processing", state.Get<string>("current_step"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddCompletedSteps()
    {
        // Arrange
        var steps = new List<string> { "step1", "step2", "step3" };

        // Act
        var state = FlowState.CreateBuilder()
            .AddCompletedSteps(steps)
            .Build();

        // Assert
        var completedSteps = state.Get<List<string>>("completed_steps");
        Assert.NotNull(completedSteps);
        Assert.Equal(3, completedSteps.Count);
        Assert.Equal(steps, completedSteps);
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddStartTime()
    {
        // Arrange
        var startTime = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        var state = FlowState.CreateBuilder()
            .AddStartTime(startTime)
            .Build();

        // Assert
        Assert.Equal(startTime, state.Get<DateTime>("start_time"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddIterationCount()
    {
        // Act
        var state = FlowState.CreateBuilder()
            .AddIterationCount(7)
            .Build();

        // Assert
        Assert.Equal(7, state.Get<int>("iteration_count"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddError()
    {
        // Act
        var state = FlowState.CreateBuilder()
            .AddError("Validation failed: Invalid input format")
            .Build();

        // Assert
        Assert.Equal("Validation failed: Invalid input format", state.Get<string>("error"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddOutput()
    {
        // Arrange
        var output = new { Status = Completed, Records = 150, Duration = "5m" };

        // Act
        var state = FlowState.CreateBuilder()
            .AddOutput(output)
            .Build();

        // Assert
        var stored = state.Get<object>("output");
        Assert.NotNull(stored);
        Assert.Equal(output, stored);
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddUserInput()
    {
        // Act
        var state = FlowState.CreateBuilder()
            .AddUserInput("Process all records")
            .Build();

        // Assert
        Assert.Equal("Process all records", state.Get<string>("user_input"));
    }

    [Fact]
    public void ShouldAddCustomState_WhenUsingBuilderAdd()
    {
        // Act
        var state = FlowState.CreateBuilder()
            .Add("custom_string", "value")
            .Add("custom_int", 42)
            .Add("custom_bool", true)
            .Add("custom_double", 3.14)
            .Add("custom_object", new { Type = "test", Count = 5 })
            .Build();

        // Assert
        Assert.Equal("value", state.Get<string>("custom_string"));
        Assert.Equal(42, state.Get<int>("custom_int"));
        Assert.True(state.Get<bool>("custom_bool"));
        Assert.Equal(3.14, state.Get<double>("custom_double"));
        Assert.NotNull(state.Get<object>("custom_object"));
    }

    [Fact]
    public void ShouldAddAllState_WhenUsingBuilderChainedCalls()
    {
        // Arrange
        var startTime = DateTime.UtcNow;
        var completedSteps = new List<string> { "init", "validate", "process" };

        // Act
        var state = FlowState.CreateBuilder()
            .AddCurrentStep("finalize")
            .AddCompletedSteps(completedSteps)
            .AddStartTime(startTime)
            .AddIterationCount(4)
            .AddUserInput("Run full pipeline")
            .AddOutput(new { Success = true })
            .Add("custom_metric", 0.95)
            .Build();

        // Assert
        Assert.Equal(7, state.Count);
        Assert.Equal("finalize", state.Get<string>("current_step"));
        Assert.Equal(completedSteps, state.Get<List<string>>("completed_steps"));
        Assert.Equal(startTime, state.Get<DateTime>("start_time"));
        Assert.Equal(4, state.Get<int>("iteration_count"));
        Assert.Equal("Run full pipeline", state.Get<string>("user_input"));
        Assert.NotNull(state.Get<object>("output"));
        Assert.Equal(0.95, state.Get<double>("custom_metric"));
    }

    [Fact]
    public void ShouldCopyExistingState_WhenCreatingBuilderFrom()
    {
        // Arrange
        var original = FlowState.CreateBuilder()
            .AddCurrentStep("step1")
            .AddIterationCount(3)
            .Add("custom", "value")
            .Build();

        // Act
        var modified = FlowState.CreateBuilderFrom(original)
            .AddCurrentStep("step2")
            .AddIterationCount(4)
            .Add("new_key", "new_value")
            .Build();

        // Assert
        Assert.Equal(3, original.Count);
        Assert.Equal("step1", original.Get<string>("current_step"));
        Assert.Equal(3, original.Get<int>("iteration_count"));

        Assert.Equal(4, modified.Count);
        Assert.Equal("step2", modified.Get<string>("current_step"));
        Assert.Equal(4, modified.Get<int>("iteration_count"));
        Assert.Equal("value", modified.Get<string>("custom"));
        Assert.Equal("new_value", modified.Get<string>("new_key"));
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithNull()
    {
        // Act
        var state = FlowState.FromDictionary(null);

        // Assert
        Assert.Same(FlowState.Empty, state);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithEmptyDictionary()
    {
        // Act
        var state = FlowState.FromDictionary([]);

        // Assert
        Assert.Same(FlowState.Empty, state);
    }

    [Fact]
    public void ShouldCreateState_WhenUsingFromDictionaryWithValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "current_step", "processing" },
            { "iteration_count", 5 },
            { "start_time", DateTime.UtcNow },
            { "user_input", "Start processing" },
            { "progress", 0.75 }
        };

        // Act
        var state = FlowState.FromDictionary(dict);

        // Assert
        Assert.Equal(5, state.Count);
        Assert.Equal("processing", state.Get<string>("current_step"));
        Assert.Equal(5, state.Get<int>("iteration_count"));
        // DateTime is a value type, it's never null - removed Assert.NotNull
        Assert.Equal("Start processing", state.Get<string>("user_input"));
        Assert.Equal(0.75, state.Get<double>("progress"));
    }

    [Fact]
    public void ShouldReturnOriginal_WhenMergingWithNullOrEmpty()
    {
        // Arrange
        var original = FlowState.CreateBuilder()
            .AddCurrentStep("step1")
            .Build();

        // Act
        var merged1 = original.Merge(null!);
        var merged2 = original.Merge([]);

        // Assert
        Assert.Same(original, merged1);
        Assert.Same(original, merged2);
    }

    [Fact]
    public void ShouldMergeValues_WhenMergingWithUpdates()
    {
        // Arrange
        var original = FlowState.CreateBuilder()
            .AddCurrentStep("step1")
            .AddIterationCount(1)
            .Add("existing", "old value")
            .Build();

        var updates = new Dictionary<string, object>
        {
            { "current_step", "step2" }, // Update existing
            { "iteration_count", 2 }, // Update existing
            { "new_key", "new value" } // Add new
        };

        // Act
        var merged = original.Merge(updates);

        // Assert
        Assert.Equal(4, merged.Count);
        Assert.Equal("step2", merged.Get<string>("current_step"));
        Assert.Equal(2, merged.Get<int>("iteration_count"));
        Assert.Equal("old value", merged.Get<string>("existing"));
        Assert.Equal("new value", merged.Get<string>("new_key"));

        // Original unchanged
        Assert.Equal("step1", original.Get<string>("current_step"));
        Assert.Equal(1, original.Get<int>("iteration_count"));
    }

    [Fact]
    public void ShouldStoreWithPrefix_WhenSettingVariable()
    {
        // Arrange
        var state = FlowState.Empty;

        // Act
        var updated = state
            .SetVariable("userName", "John Doe")
            .SetVariable("userAge", 30);

        // Assert
        Assert.Equal("John Doe", updated.Get<string>("variables.userName"));
        Assert.Equal(30, updated.Get<int>("variables.userAge"));
    }

    [Fact]
    public void ShouldRetrieveWithPrefix_WhenGettingVariable()
    {
        // Arrange
        var state = FlowState.CreateBuilder()
            .Add("variables.config", "production")
            .Add("variables.timeout", 300)
            .Build();

        // Act
        var config = state.GetVariable<string>("config");
        var timeout = state.Get<int?>("variables.timeout");

        // Assert
        Assert.Equal("production", config);
        Assert.Equal(300, timeout); // Get<int?> should return the actual value
    }

    [Fact]
    public void ShouldStoreWithPrefix_WhenUsingSetSharedState()
    {
        // Arrange
        var state = FlowState.Empty;

        // Act
        var updated = state
            .SetSharedState("globalCounter", 100)
            .SetSharedState("globalFlag", true);

        // Assert
        Assert.Equal(100, updated.Get<int>("shared.globalCounter"));
        Assert.True(updated.Get<bool>("shared.globalFlag"));
    }

    [Fact]
    public void ShouldRetrieveWithPrefix_WhenUsingGetSharedState()
    {
        // Arrange
        var state = FlowState.CreateBuilder()
            .Add("shared.connectionString", "server=localhost")
            .Add("shared.maxRetries", 5)
            .Build();

        // Act
        var connectionString = state.GetSharedState<string>("connectionString");
        var maxRetries = state.Get<int>("shared.maxRetries");

        // Assert
        Assert.Equal("server=localhost", connectionString);
        // int is value type, cannot use Assert.Null - checking with Get instead
        Assert.Equal(5, state.Get<int>("shared.maxRetries"));
    }

    [Fact]
    public void ShouldCreateCorrectSnapshot_WhenUsingCreateSnapshot()
    {
        // Arrange
        var state = FlowState.CreateBuilder()
            .AddCurrentStep("processing")
            .AddIterationCount(5)
            .AddOutput("intermediate result")
            .Build();

        var beforeSnapshot = DateTime.UtcNow;

        // Act
        var snapshot = state.CreateSnapshot("checkpoint1");

        var afterSnapshot = DateTime.UtcNow;

        // Assert
        Assert.Equal("checkpoint1", snapshot.Label);
        Assert.True(snapshot.Timestamp >= beforeSnapshot);
        Assert.True(snapshot.Timestamp <= afterSnapshot);
        Assert.Equal(3, snapshot.State.Count);
        Assert.Equal("processing", snapshot.State["current_step"]);
        Assert.Equal(5, snapshot.State["iteration_count"]);
        Assert.Equal("intermediate result", snapshot.State["output"]);
    }

    [Fact]
    public void ShouldStoreSnapshotInState_WhenUsingSaveSnapshot()
    {
        // Arrange
        var state = FlowState.CreateBuilder()
            .AddCurrentStep("step1")
            .AddIterationCount(3)
            .Build();

        // Act
        var withSnapshot = state.SaveSnapshot("before_processing");

        // Assert
        Assert.Equal(3, withSnapshot.Count); // Original 2 + 1 snapshot

        var snapshot = withSnapshot.Get<FlowStateSnapshot>("snapshots.before_processing");
        Assert.NotNull(snapshot);
        Assert.Equal("before_processing", snapshot.Label);
        Assert.Equal(2, snapshot.State.Count);
        Assert.Equal("step1", snapshot.State["current_step"]);
        Assert.Equal(3, snapshot.State["iteration_count"]);
    }

    #endregion

    #region FlowStateValue Tests

    [Fact]
    public void ShouldCreate_WhenUsingFlowStateValueFromWithValidValue()
    {
        // Act
        var stringValue = FlowStateValue.From("test");
        var intValue = FlowStateValue.From(42);
        var boolValue = FlowStateValue.From(true);
        var doubleValue = FlowStateValue.From(3.14);
        var dateValue = FlowStateValue.From(DateTime.UtcNow);
        var listValue = FlowStateValue.From(new List<string> { "a", "b", "c" });

        // Assert
        Assert.NotNull(stringValue);
        Assert.NotNull(intValue);
        Assert.NotNull(boolValue);
        Assert.NotNull(doubleValue);
        Assert.NotNull(dateValue);
        Assert.NotNull(listValue);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingFlowStateValueFromWithNull()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => FlowStateValue.From(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingFlowStateValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = FlowStateValue.From("test string");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("test string", result);
    }

    [Fact]
    public void ShouldConvert_WhenUsingFlowStateValueGettingValueWithConvertibleType()
    {
        // Arrange
        var intValue = FlowStateValue.From(123);

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
    public void ShouldThrowInvalidCastException_WhenUsingFlowStateValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = FlowStateValue.From("not a number");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(
            () => value.GetValue<int>());
        Assert.Contains("Cannot convert flow state value", exception.Message);
        Assert.Contains("String to Int32", exception.Message);
    }

    [Fact]
    public void ShouldReturnOriginalValue_WhenUsingFlowStateValueUsingRawValue()
    {
        // Arrange
        var original = new { Name = "Test", Value = 123 };
        var value = FlowStateValue.From(original);

        // Act
        var raw = value.RawValue;

        // Assert
        Assert.Same(original, raw);
    }

    [Fact]
    public void ShouldReturnCorrectType_WhenUsingFlowStateValueUsingValueType()
    {
        // Arrange
        var stringValue = FlowStateValue.From("test");
        var intValue = FlowStateValue.From(42);
        var boolValue = FlowStateValue.From(true);
        var listValue = FlowStateValue.From(new List<int> { 1, 2, 3 });

        // Act & Assert
        Assert.Equal(typeof(string), stringValue.ValueType);
        Assert.Equal(typeof(int), intValue.ValueType);
        Assert.Equal(typeof(bool), boolValue.ValueType);
        Assert.Equal(typeof(List<int>), listValue.ValueType);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldFullFlowExecution_WhenUsingComplexScenario()
    {
        // Simulate a complete flow execution
        var flow = FlowState.Empty;

        // Initialize flow
        flow = flow.Merge(new Dictionary<string, object>
        {
            { "flow_id", Guid.NewGuid().ToString() },
            { "flow_name", "DataProcessingPipeline" },
            { "start_time", DateTime.UtcNow }
        });

        // Step 1: Data Loading
        flow = FlowState.CreateBuilderFrom(flow)
            .AddCurrentStep("data_loading")
            .AddIterationCount(1)
            .Build();

        flow = flow.SetVariable("dataSource", "database")
            .SetVariable("recordCount", 1000);

        // Step 2: Data Validation
        flow = FlowState.CreateBuilderFrom(flow)
            .AddCurrentStep("data_validation")
            .AddIterationCount(2)
            .AddCompletedSteps(new List<string> { "data_loading" })
            .Build();

        // Save snapshot before processing
        flow = flow.SaveSnapshot("after_validation");

        // Step 3: Data Processing
        flow = FlowState.CreateBuilderFrom(flow)
            .AddCurrentStep("data_processing")
            .AddIterationCount(3)
            .AddCompletedSteps(new List<string> { "data_loading", "data_validation" })
            .Build();

        flow = flow.SetSharedState("processedRecords", 950)
            .SetSharedState("errors", 50);

        // Step 4: Complete
        flow = FlowState.CreateBuilderFrom(flow)
            .AddCurrentStep("complete")
            .AddCompletedSteps(new List<string> { "data_loading", "data_validation", "data_processing" })
            .AddOutput(new
            {
                TotalRecords = 1000,
                ProcessedRecords = 950,
                Errors = 50,
                SuccessRate = 0.95
            })
            .Build();

        // Assert final state
        Assert.Equal("complete", flow.Get<string>("current_step"));
        Assert.Equal(3, flow.Get<int>("iteration_count"));
        Assert.Equal(1000, flow.Get<int>("variables.recordCount"));
        Assert.Equal(950, flow.Get<int>("shared.processedRecords"));
        Assert.NotNull(flow.Get<FlowStateSnapshot>("snapshots.after_validation"));
        Assert.NotNull(flow.Get<object>("output"));

        var completedSteps = flow.Get<List<string>>("completed_steps");
        Assert.Equal(3, completedSteps!.Count);
    }

    [Fact]
    public void ShouldImmutabilityCheck_WhenUsingComplexScenario()
    {
        // Arrange
        var original = FlowState.CreateBuilder()
            .AddCurrentStep("step1")
            .AddIterationCount(1)
            .Build();

        // Act - Multiple modifications
        var modified1 = original.Set("current_step", "step2");
        var modified2 = modified1.Set("iteration_count", 2);
        var modified3 = modified2.Set("new_key", "value");

        // Assert - Each instance is independent
        Assert.Equal(2, original.Count);
        Assert.Equal("step1", original.Get<string>("current_step"));
        Assert.Equal(1, original.Get<int>("iteration_count"));

        Assert.Equal(2, modified1.Count);
        Assert.Equal("step2", modified1.Get<string>("current_step"));
        Assert.Equal(1, modified1.Get<int>("iteration_count"));

        Assert.Equal(2, modified2.Count);
        Assert.Equal("step2", modified2.Get<string>("current_step"));
        Assert.Equal(2, modified2.Get<int>("iteration_count"));

        Assert.Equal(3, modified3.Count);
        Assert.Equal("step2", modified3.Get<string>("current_step"));
        Assert.Equal(2, modified3.Get<int>("iteration_count"));
        Assert.Equal("value", modified3.Get<string>("new_key"));
    }

    [Fact]
    public void ShouldErrorHandlingFlow_WhenUsingComplexScenario()
    {
        // Simulate a flow with error handling and retries
        var flow = FlowState.CreateBuilder()
            .AddCurrentStep("risky_operation")
            .AddIterationCount(1)
            .AddStartTime(DateTime.UtcNow)
            .Build();

        // First attempt fails
        flow = flow.Set("iteration_count", 2)
            .Set("error", "Connection timeout");

        // Save error state
        flow = flow.SaveSnapshot("error_state_1");

        // Retry with different parameters
        flow = flow.SetVariable("retryDelay", 1000)
            .SetVariable("maxRetries", 3)
            .Remove("error");

        // Second attempt succeeds
        flow = flow.Set("iteration_count", 3)
            .Set("current_step", "completed")
            .Set("output", new { Status = Success, Attempts = 2 });

        // Assert
        Assert.Equal("completed", flow.Get<string>("current_step"));
        Assert.Equal(3, flow.Get<int>("iteration_count"));
        Assert.False(flow.ContainsKey("error"));
        Assert.NotNull(flow.Get<FlowStateSnapshot>("snapshots.error_state_1"));

        var errorSnapshot = flow.Get<FlowStateSnapshot>("snapshots.error_state_1");
        Assert.Equal("Connection timeout", errorSnapshot!.State!["error"]);
    }

    [Fact]
    public void ShouldRoundTripConversion_WhenUsingComplexScenario()
    {
        // Arrange
        var original = FlowState.CreateBuilder()
            .AddCurrentStep("processing")
            .AddCompletedSteps(new List<string> { "init", "validate" })
            .AddStartTime(DateTime.UtcNow)
            .AddIterationCount(5)
            .AddUserInput("Execute all")
            .AddOutput(new { Result = "Partial" })
            .Add("custom_data", new Dictionary<string, int> { { "a", 1 }, { "b", 2 } })
            .Build();

        // Add variables and shared state
        original = original
            .SetVariable("config", "production")
            .SetSharedState("cache", new { Size = 100, Type = "LRU" });

        // Act
        var dict = original.ToDictionary();
        var restored = FlowState.FromDictionary(dict);

        // Assert
        Assert.Equal(original.Count, restored.Count);
        Assert.Equal("processing", restored.Get<string>("current_step"));
        Assert.Equal(5, restored.Get<int>("iteration_count"));
        Assert.NotNull(restored.Get<List<string>>("completed_steps"));
        // DateTime is a value type, it's never null - removed Assert.NotNull
        Assert.Equal("Execute all", restored.Get<string>("user_input"));
        Assert.NotNull(restored.Get<object>("output"));
        Assert.NotNull(restored.Get<object>("custom_data"));
        Assert.Equal("production", restored.Get<string>("variables.config"));
        Assert.NotNull(restored.Get<object>("shared.cache"));
    }

    #endregion
}
