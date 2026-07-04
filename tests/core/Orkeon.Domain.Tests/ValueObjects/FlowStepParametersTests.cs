using Orkeon.Domain.Flows.ValueObjects;

using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;
namespace Orkeon.Domain.Tests.ValueObjects;

public class FlowStepParametersTests
{
    private static readonly int[] Int123 = [1, 2, 3];
    private static readonly int[] s_intArray123 = [1, 2, 3];

    #region FlowStepParameters Tests

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var parameters = FlowStepParameters.Empty;

        // Assert
        Assert.NotNull(parameters);
        Assert.Empty(parameters.Keys);
        Assert.Equal(0, parameters.Count);
        Assert.False(parameters.ContainsKey("anyKey"));
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingWithExistingKey()
    {
        // Arrange
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var parameters = FlowStepParameters.CreateBuilder()
            .AddAgent(agentId)
            .AddTask(taskId)
            .AddLoop(5)
            .Build();

        // Act & Assert
        Assert.Equal(agentId, parameters.Get<AgentId>("agent_id"));
        Assert.Equal(taskId, parameters.Get<TaskId>("task_id"));
        Assert.Equal(5, parameters.Get<int>("max_iterations"));
    }

    [Fact]
    public void ShouldReturnDefault_WhenGettingWithNonExistentKey()
    {
        // Arrange
        var parameters = FlowStepParameters.Empty;

        // Act & Assert
        Assert.Null(parameters.Get<string>("missing"));
        Assert.Equal(0, parameters.Get<int>("missing"));
        Assert.False(parameters.Get<bool>("missing"));
        Assert.Equal(0.0, parameters.Get<double>("missing"));
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContainsKey()
    {
        // Arrange
        var parameters = FlowStepParameters.CreateBuilder()
            .AddTool("DataProcessor")
            .Build();

        // Act & Assert
        Assert.True(parameters.ContainsKey("tool"));
        Assert.False(parameters.ContainsKey("missing_key"));
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenUsingKeys()
    {
        // Arrange
        var parameters = FlowStepParameters.CreateBuilder()
            .AddAgent(AgentId.Create())
            .AddTask(TaskId.Create())
            .AddCondition("status == 'ready'")
            .Build();

        // Act
        var keys = parameters.Keys.ToList();

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("agent_id", keys);
        Assert.Contains("task_id", keys);
        Assert.Contains("condition", keys);
    }

    [Fact]
    public void ShouldReturnNumberOfParameters_WhenCounting()
    {
        // Arrange
        var parameters = FlowStepParameters.CreateBuilder()
            .AddAgent(AgentId.Create())
            .AddTask(TaskId.Create())
            .AddTool("tool-1")
            .AddLoop(10)
            .Build();

        // Act & Assert
        Assert.Equal(4, parameters.Count);
    }

    [Fact]
    public void ShouldCreateNewInstanceWithUpdatedValue_WhenSetting()
    {
        // Arrange
        var original = FlowStepParameters.CreateBuilder()
            .AddLoop(3)
            .Build();

        // Act
        var updated = original.Set("max_iterations", 5);
        var added = updated.Set("new_param", "value");

        // Assert
        Assert.NotSame(original, updated);
        Assert.NotSame(updated, added);

        Assert.Equal(3, original.Get<int>("max_iterations"));
        Assert.Equal(5, updated.Get<int>("max_iterations"));
        Assert.Equal("value", added.Get<string>("new_param"));

        Assert.Equal(1, original.Count);
        Assert.Equal(1, updated.Count);
        Assert.Equal(2, added.Count);
    }

    [Fact]
    public void ShouldReturnAllParameters_WhenUsingToDictionary()
    {
        // Arrange
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var parameters = FlowStepParameters.CreateBuilder()
            .AddAgent(agentId)
            .AddTask(taskId)
            .AddTool("Analyzer")
            .AddCondition("count > 0")
            .AddLoop(3)
            .Build();

        // Act
        var dict = parameters.ToDictionary();

        // Assert
        Assert.Equal(5, dict.Count);
        Assert.Equal(agentId, dict["agent_id"]);
        Assert.Equal(taskId, dict["task_id"]);
        Assert.Equal("Analyzer", dict["tool"]);
        Assert.Equal("count > 0", dict["condition"]);
        Assert.Equal(3, dict["max_iterations"]);
    }

    [Fact]
    public void ShouldSetWithPrefix_WhenUsingBuilderAddInput()
    {
        // Act
        var parameters = FlowStepParameters.CreateBuilder()
            .AddInput("fileName", "data.csv")
            .AddInput("encoding", "utf-8")
            .AddInput("hasHeader", true)
            .Build();

        // Assert
        Assert.Equal("data.csv", parameters.Get<string>("input.fileName"));
        Assert.Equal("utf-8", parameters.Get<string>("input.encoding"));
        Assert.True(parameters.Get<bool>("input.hasHeader"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddCondition()
    {
        // Act
        var parameters = FlowStepParameters.CreateBuilder()
            .AddCondition("response.status == 200 && response.data != null")
            .Build();

        // Assert
        Assert.Equal("response.status == 200 && response.data != null",
            parameters.Get<string>("condition"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddLoop()
    {
        // Act
        var parameters = FlowStepParameters.CreateBuilder()
            .AddLoop(100)
            .Build();

        // Assert
        Assert.Equal(100, parameters.Get<int>("max_iterations"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddAgent()
    {
        // Arrange
        var agentId = AgentId.Create();

        // Act
        var parameters = FlowStepParameters.CreateBuilder()
            .AddAgent(agentId)
            .Build();

        // Assert
        Assert.Equal(agentId, parameters.Get<AgentId>("agent_id"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddTask()
    {
        // Arrange
        var taskId = TaskId.Create();

        // Act
        var parameters = FlowStepParameters.CreateBuilder()
            .AddTask(taskId)
            .Build();

        // Assert
        Assert.Equal(taskId, parameters.Get<TaskId>("task_id"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingBuilderAddTool()
    {
        // Act
        var parameters = FlowStepParameters.CreateBuilder()
            .AddTool("WebScraper")
            .Build();

        // Assert
        Assert.Equal("WebScraper", parameters.Get<string>("tool"));
    }

    [Fact]
    public void ShouldAddCustomParameters_WhenUsingBuilderAdd()
    {
        // Act
        var parameters = FlowStepParameters.CreateBuilder()
            .Add("custom_string", "value")
            .Add("custom_int", 42)
            .Add("custom_bool", true)
            .Add("custom_double", 3.14)
            .Add("custom_array", Int123)
            .Build();

        // Assert
        Assert.Equal("value", parameters.Get<string>("custom_string"));
        Assert.Equal(42, parameters.Get<int>("custom_int"));
        Assert.True(parameters.Get<bool>("custom_bool"));
        Assert.Equal(3.14, parameters.Get<double>("custom_double"));
        Assert.Equal(s_intArray123, parameters.Get<int[]>("custom_array"));
    }

    [Fact]
    public void ShouldAddAllParameters_WhenUsingBuilderChainedCalls()
    {
        // Arrange
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();

        // Act
        var parameters = FlowStepParameters.CreateBuilder()
            .AddAgent(agentId)
            .AddTask(taskId)
            .AddTool("MainProcessor")
            .AddCondition("isReady == true")
            .AddLoop(50)
            .AddInput("dataSource", "database")
            .AddInput("batchSize", 100)
            .Add("timeout", 30000)
            .Build();

        // Assert
        Assert.Equal(8, parameters.Count);
        Assert.Equal(agentId, parameters.Get<AgentId>("agent_id"));
        Assert.Equal(taskId, parameters.Get<TaskId>("task_id"));
        Assert.Equal("MainProcessor", parameters.Get<string>("tool"));
        Assert.Equal("isReady == true", parameters.Get<string>("condition"));
        Assert.Equal(50, parameters.Get<int>("max_iterations"));
        Assert.Equal("database", parameters.Get<string>("input.dataSource"));
        Assert.Equal(100, parameters.Get<int>("input.batchSize"));
        Assert.Equal(30000, parameters.Get<int>("timeout"));
    }

    [Fact]
    public void ShouldKeepLastValue_WhenUsingBuilderOverwriteExistingKey()
    {
        // Act
        var parameters = FlowStepParameters.CreateBuilder()
            .AddLoop(10)
            .AddLoop(20)
            .Build();

        // Assert
        Assert.Equal(20, parameters.Get<int>("max_iterations"));
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithNull()
    {
        // Act
        var parameters = FlowStepParameters.FromDictionary(null);

        // Assert
        Assert.Same(FlowStepParameters.Empty, parameters);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithEmptyDictionary()
    {
        // Act
        var parameters = FlowStepParameters.FromDictionary([]);

        // Assert
        Assert.Same(FlowStepParameters.Empty, parameters);
    }

    [Fact]
    public void ShouldCreateParameters_WhenUsingFromDictionaryWithValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "agent_id", "agent-999" },
            { "task_id", "task-888" },
            { "tool", "Calculator" },
            { "max_iterations", 15 },
            { "condition", "value > threshold" }
        };

        // Act
        var parameters = FlowStepParameters.FromDictionary(dict);

        // Assert
        Assert.Equal(5, parameters.Count);
        Assert.Equal("agent-999", parameters.Get<string>("agent_id"));
        Assert.Equal("task-888", parameters.Get<string>("task_id"));
        Assert.Equal("Calculator", parameters.Get<string>("tool"));
        Assert.Equal(15, parameters.Get<int>("max_iterations"));
        Assert.Equal("value > threshold", parameters.Get<string>("condition"));
    }

    [Fact]
    public void ShouldPreserveValues_WhenUsingRoundTripToDictionaryFromDictionary()
    {
        // Arrange
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var original = FlowStepParameters.CreateBuilder()
            .AddAgent(agentId)
            .AddTask(taskId)
            .AddTool("RTTool")
            .AddCondition("roundtrip == true")
            .AddLoop(7)
            .Add("custom_value", 123.45)
            .Build();

        // Act
        var dict = original.ToDictionary();
        var restored = FlowStepParameters.FromDictionary(dict);

        // Assert
        Assert.Equal(original.Count, restored.Count);
        Assert.Equal(agentId, restored.Get<AgentId>("agent_id"));
        Assert.Equal(taskId, restored.Get<TaskId>("task_id"));
        Assert.Equal("RTTool", restored.Get<string>("tool"));
        Assert.Equal("roundtrip == true", restored.Get<string>("condition"));
        Assert.Equal(7, restored.Get<int>("max_iterations"));
        Assert.Equal(123.45, restored.Get<double>("custom_value"));
    }

    #endregion

    #region FlowStepMetadata Tests

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingFlowStepMetadataWithEmpty()
    {
        // Act
        var metadata = FlowStepMetadata.Empty;

        // Assert
        Assert.NotNull(metadata);
        Assert.Empty(metadata.Keys);
        Assert.Equal(0, metadata.Count);
        Assert.False(metadata.ContainsKey("anyKey"));
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingFlowStepMetadataGettingWithExistingKey()
    {
        // Arrange
        var metadata = FlowStepMetadata.CreateBuilder()
            .AddDescription("Process user data")
            .AddCategory("data-processing")
            .AddPriority(1)
            .Build();

        // Act & Assert
        Assert.Equal("Process user data", metadata.Get<string>("description"));
        Assert.Equal("data-processing", metadata.Get<string>("category"));
        Assert.Equal(1, metadata.Get<int>("priority"));
    }

    [Fact]
    public void ShouldReturnDefault_WhenUsingFlowStepMetadataGettingWithNonExistentKey()
    {
        // Arrange
        var metadata = FlowStepMetadata.Empty;

        // Act & Assert
        Assert.Null(metadata.Get<string>("missing"));
        Assert.Equal(0, metadata.Get<int>("missing"));
        Assert.Null(metadata.Get<List<string>>("missing"));
    }

    [Fact]
    public void ShouldCreateNewInstance_WhenUsingFlowStepMetadataSetting()
    {
        // Arrange
        var original = FlowStepMetadata.CreateBuilder()
            .AddPriority(2)
            .Build();

        // Act
        var updated = original.Set("priority", 1);
        var added = updated.Set("new_meta", "value");

        // Assert
        Assert.NotSame(original, updated);
        Assert.NotSame(updated, added);

        Assert.Equal(2, original.Get<int>("priority"));
        Assert.Equal(1, updated.Get<int>("priority"));
        Assert.Equal("value", added.Get<string>("new_meta"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingFlowStepMetadataUsingBuilderAddDescription()
    {
        // Act
        var metadata = FlowStepMetadata.CreateBuilder()
            .AddDescription("This step validates input data and returns validation result")
            .Build();

        // Assert
        Assert.Equal("This step validates input data and returns validation result",
            metadata.Get<string>("description"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingFlowStepMetadataUsingBuilderAddCategory()
    {
        // Act
        var metadata = FlowStepMetadata.CreateBuilder()
            .AddCategory("validation")
            .Build();

        // Assert
        Assert.Equal("validation", metadata.Get<string>("category"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingFlowStepMetadataUsingBuilderAddPriority()
    {
        // Act
        var metadata = FlowStepMetadata.CreateBuilder()
            .AddPriority(5)
            .Build();

        // Assert
        Assert.Equal(5, metadata.Get<int>("priority"));
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingFlowStepMetadataUsingBuilderAddTags()
    {
        // Arrange
        var tags = new List<string> { "critical", "performance", "async" };

        // Act
        var metadata = FlowStepMetadata.CreateBuilder()
            .AddTags(tags)
            .Build();

        // Assert
        var storedTags = metadata.Get<List<string>>("tags");
        Assert.NotNull(storedTags);
        Assert.Equal(3, storedTags.Count);
        Assert.Equal(tags, storedTags);
    }

    [Fact]
    public void ShouldSetCorrectValue_WhenUsingFlowStepMetadataUsingBuilderAddCreatedAt()
    {
        // Arrange
        var createdAt = new DateTime(2024, 1, 15, 14, 30, 0, DateTimeKind.Utc);

        // Act
        var metadata = FlowStepMetadata.CreateBuilder()
            .AddCreatedAt(createdAt)
            .Build();

        // Assert
        Assert.Equal(createdAt, metadata.Get<DateTime>("created_at"));
    }

    [Fact]
    public void ShouldAddAllMetadata_WhenUsingFlowStepMetadataUsingBuilderChainedCalls()
    {
        // Arrange
        var tags = new List<string> { "async", "retry-enabled" };
        var createdAt = DateTime.UtcNow;

        // Act
        var metadata = FlowStepMetadata.CreateBuilder()
            .AddDescription("Complex data transformation step")
            .AddCategory("transformation")
            .AddPriority(2)
            .AddTags(tags)
            .AddCreatedAt(createdAt)
            .Add("author", "system")
            .Add("version", "1.2.0")
            .Build();

        // Assert
        Assert.Equal(7, metadata.Count);
        Assert.Equal("Complex data transformation step", metadata.Get<string>("description"));
        Assert.Equal("transformation", metadata.Get<string>("category"));
        Assert.Equal(2, metadata.Get<int>("priority"));
        Assert.Equal(tags, metadata.Get<List<string>>("tags"));
        Assert.Equal(createdAt, metadata.Get<DateTime>("created_at"));
        Assert.Equal("system", metadata.Get<string>("author"));
        Assert.Equal("1.2.0", metadata.Get<string>("version"));
    }

    [Fact]
    public void ShouldCreateMetadata_WhenUsingFlowStepMetadataFromDictionary()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "description", "Test step" },
            { "category", "testing" },
            { "priority", 3 },
            { "tags", new List<string> { "test", "qa" } }
        };

        // Act
        var metadata = FlowStepMetadata.FromDictionary(dict);

        // Assert
        Assert.Equal(4, metadata.Count);
        Assert.Equal("Test step", metadata.Get<string>("description"));
        Assert.Equal("testing", metadata.Get<string>("category"));
        Assert.Equal(3, metadata.Get<int>("priority"));
        Assert.NotNull(metadata.Get<List<string>>("tags"));
    }

    #endregion

    #region FlowStepParameterValue Tests

    [Fact]
    public void ShouldCreate_WhenUsingFlowStepParameterValueFromWithValidValue()
    {
        // Act
        var stringValue = FlowStepParameterValue.From("test");
        var intValue = FlowStepParameterValue.From(42);
        var boolValue = FlowStepParameterValue.From(true);
        var doubleValue = FlowStepParameterValue.From(3.14);
        var dateValue = FlowStepParameterValue.From(DateTime.UtcNow);

        // Assert
        Assert.NotNull(stringValue);
        Assert.NotNull(intValue);
        Assert.NotNull(boolValue);
        Assert.NotNull(doubleValue);
        Assert.NotNull(dateValue);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingFlowStepParameterValueFromWithNull()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => FlowStepParameterValue.From(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingFlowStepParameterValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = FlowStepParameterValue.From("test string");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("test string", result);
    }

    [Fact]
    public void ShouldConvert_WhenUsingFlowStepParameterValueGettingValueWithConvertibleType()
    {
        // Arrange
        var intValue = FlowStepParameterValue.From(123);

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
    public void ShouldThrowInvalidCastException_WhenUsingFlowStepParameterValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = FlowStepParameterValue.From("not a number");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(
            () => value.GetValue<int>());
        Assert.Contains("Cannot convert flow step parameter value", exception.Message);
    }

    [Fact]
    public void ShouldReturnOriginalValue_WhenUsingFlowStepParameterValueUsingRawValue()
    {
        // Arrange
        var original = new { Name = "Test", Value = 123 };
        var value = FlowStepParameterValue.From(original);

        // Act
        var raw = value.RawValue;

        // Assert
        Assert.Same(original, raw);
    }

    [Fact]
    public void ShouldReturnCorrectType_WhenUsingFlowStepParameterValueUsingValueType()
    {
        // Arrange
        var stringValue = FlowStepParameterValue.From("test");
        var intValue = FlowStepParameterValue.From(42);
        var listValue = FlowStepParameterValue.From(new List<string>());

        // Act & Assert
        Assert.Equal(typeof(string), stringValue.ValueType);
        Assert.Equal(typeof(int), intValue.ValueType);
        Assert.Equal(typeof(List<string>), listValue.ValueType);
    }

    #endregion

    #region FlowStepMetadataValue Tests

    [Fact]
    public void ShouldCreate_WhenUsingFlowStepMetadataValueFromWithValidValue()
    {
        // Act
        var stringValue = FlowStepMetadataValue.From("metadata");
        var listValue = FlowStepMetadataValue.From(new List<string> { "tag1", "tag2" });
        var dateValue = FlowStepMetadataValue.From(DateTime.UtcNow);

        // Assert
        Assert.NotNull(stringValue);
        Assert.NotNull(listValue);
        Assert.NotNull(dateValue);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingFlowStepMetadataValueFromWithNull()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => FlowStepMetadataValue.From(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingFlowStepMetadataValueGettingValueWithCorrectType()
    {
        // Arrange
        var tags = new List<string> { "important", "urgent" };
        var value = FlowStepMetadataValue.From(tags);

        // Act
        var result = value.GetValue<List<string>>();

        // Assert
        Assert.Equal(tags, result);
    }

    #endregion

    #region FlowStepParameters Equality Tests

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoFlowStepParametersWithSameValues()
    {
        // Arrange
        var parameters1 = FlowStepParameters.CreateBuilder()
            .AddTool("Tool1")
            .AddLoop(5)
            .AddCondition("x > 0")
            .Build();

        var parameters2 = FlowStepParameters.CreateBuilder()
            .AddTool("Tool1")
            .AddLoop(5)
            .AddCondition("x > 0")
            .Build();

        // Act & Assert
        Assert.Equal(parameters1, parameters2);
        Assert.True(parameters1.Equals(parameters2));
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoFlowStepParametersWithDifferentValues()
    {
        // Arrange
        var parameters1 = FlowStepParameters.CreateBuilder()
            .AddLoop(5)
            .Build();

        var parameters2 = FlowStepParameters.CreateBuilder()
            .AddLoop(10)
            .Build();

        // Act & Assert
        Assert.NotEqual(parameters1, parameters2);
        Assert.False(parameters1.Equals(parameters2));
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingFlowStepParametersWithDifferentCounts()
    {
        // Arrange
        var parameters1 = FlowStepParameters.CreateBuilder()
            .AddLoop(5)
            .Build();

        var parameters2 = FlowStepParameters.CreateBuilder()
            .AddLoop(5)
            .AddTool("Extra")
            .Build();

        // Act & Assert
        Assert.NotEqual(parameters1, parameters2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingFlowStepParametersWithNull()
    {
        // Arrange
        var parameters = FlowStepParameters.CreateBuilder()
            .AddLoop(5)
            .Build();

        // Act & Assert
        Assert.False(parameters.Equals(null));
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingFlowStepParametersWithSameReference()
    {
        // Arrange
        var parameters = FlowStepParameters.CreateBuilder()
            .AddLoop(5)
            .Build();

        // Act & Assert
        Assert.True(parameters.Equals(parameters));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingFlowStepParametersWithDifferentObjectType()
    {
        // Arrange
        var parameters = FlowStepParameters.CreateBuilder()
            .AddLoop(5)
            .Build();

        // Act & Assert
        Assert.False(parameters.Equals("not FlowStepParameters"));
    }

    [Fact]
    public void ShouldHaveSameHashCode_WhenTwoFlowStepParametersAreEqual()
    {
        // Arrange
        var parameters1 = FlowStepParameters.CreateBuilder()
            .AddTool("MyTool")
            .AddLoop(3)
            .Build();

        var parameters2 = FlowStepParameters.CreateBuilder()
            .AddTool("MyTool")
            .AddLoop(3)
            .Build();

        // Act & Assert
        Assert.Equal(parameters1.GetHashCode(), parameters2.GetHashCode());
    }

    [Fact]
    public void ShouldHaveDifferentHashCode_WhenTwoFlowStepParametersAreDifferent()
    {
        // Arrange
        var parameters1 = FlowStepParameters.CreateBuilder()
            .AddTool("Tool1")
            .Build();

        var parameters2 = FlowStepParameters.CreateBuilder()
            .AddTool("Tool2")
            .Build();

        // Act & Assert
        Assert.NotEqual(parameters1.GetHashCode(), parameters2.GetHashCode());
    }

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoEmptyFlowStepParameters()
    {
        // Arrange
        var parameters1 = FlowStepParameters.Empty;
        var parameters2 = FlowStepParameters.Empty;

        // Act & Assert
        Assert.Equal(parameters1, parameters2);
        Assert.Equal(parameters1.GetHashCode(), parameters2.GetHashCode());
    }

    #endregion

    #region FlowStepMetadata Equality Tests

    [Fact]
    public void ShouldBeEqual_WhenComparingTwoFlowStepMetadataWithSameValues()
    {
        // Arrange
        var metadata1 = FlowStepMetadata.CreateBuilder()
            .AddDescription("Step 1")
            .AddCategory("data")
            .AddPriority(3)
            .Build();

        var metadata2 = FlowStepMetadata.CreateBuilder()
            .AddDescription("Step 1")
            .AddCategory("data")
            .AddPriority(3)
            .Build();

        // Act & Assert
        Assert.Equal(metadata1, metadata2);
        Assert.True(metadata1.Equals(metadata2));
    }

    [Fact]
    public void ShouldNotBeEqual_WhenComparingTwoFlowStepMetadataWithDifferentValues()
    {
        // Arrange
        var metadata1 = FlowStepMetadata.CreateBuilder()
            .AddPriority(1)
            .Build();

        var metadata2 = FlowStepMetadata.CreateBuilder()
            .AddPriority(2)
            .Build();

        // Act & Assert
        Assert.NotEqual(metadata1, metadata2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingFlowStepMetadataWithNull()
    {
        // Arrange
        var metadata = FlowStepMetadata.CreateBuilder()
            .AddDescription("test")
            .Build();

        // Act & Assert
        Assert.False(metadata.Equals(null));
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingFlowStepMetadataWithSameReference()
    {
        // Arrange
        var metadata = FlowStepMetadata.CreateBuilder()
            .AddDescription("test")
            .Build();

        // Act & Assert
        Assert.True(metadata.Equals(metadata));
    }

    [Fact]
    public void ShouldHaveSameHashCode_WhenTwoFlowStepMetadataAreEqual()
    {
        // Arrange
        var metadata1 = FlowStepMetadata.CreateBuilder()
            .AddCategory("ETL")
            .AddPriority(1)
            .Build();

        var metadata2 = FlowStepMetadata.CreateBuilder()
            .AddCategory("ETL")
            .AddPriority(1)
            .Build();

        // Act & Assert
        Assert.Equal(metadata1.GetHashCode(), metadata2.GetHashCode());
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFlowStepMetadataFromDictionaryWithNull()
    {
        // Act
        var metadata = FlowStepMetadata.FromDictionary(null);

        // Assert
        Assert.Same(FlowStepMetadata.Empty, metadata);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFlowStepMetadataFromDictionaryWithEmptyDictionary()
    {
        // Act
        var metadata = FlowStepMetadata.FromDictionary([]);

        // Assert
        Assert.Same(FlowStepMetadata.Empty, metadata);
    }

    [Fact]
    public void ShouldReturnAllValues_WhenUsingFlowStepMetadataToDictionary()
    {
        // Arrange
        var metadata = FlowStepMetadata.CreateBuilder()
            .AddDescription("Desc")
            .AddCategory("Cat")
            .AddPriority(5)
            .Build();

        // Act
        var dict = metadata.ToDictionary();

        // Assert
        Assert.Equal(3, dict.Count);
        Assert.Equal("Desc", dict["description"]);
        Assert.Equal("Cat", dict["category"]);
        Assert.Equal(5, dict["priority"]);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldFlowStepWithFullConfiguration_WhenUsingComplexScenario()
    {
        // Arrange - Create complex parameters
        var agentId = AgentId.Create();
        var taskId = TaskId.Create();
        var parameters = FlowStepParameters.CreateBuilder()
            .AddAgent(agentId)
            .AddTask(taskId)
            .AddTool("DataTransformer")
            .AddCondition("inputData.isValid && inputData.rows > 0")
            .AddLoop(1000)
            .AddInput("source", "database")
            .AddInput(ParamQuery, "SELECT * FROM users")
            .AddInput("timeout", 30)
            .Add("retry_policy", new { MaxRetries = 3, Delay = 1000 })
            .Add("output_format", "json")
            .Build();

        // Arrange - Create metadata
        var metadata = FlowStepMetadata.CreateBuilder()
            .AddDescription("Transforms user data from database to JSON format")
            .AddCategory("ETL")
            .AddPriority(1)
            .AddTags(["database", "transformation", "json"])
            .AddCreatedAt(DateTime.UtcNow)
            .Add("estimated_duration", "5m")
            .Add("requires_auth", true)
            .Build();

        // Assert parameters
        Assert.Equal(10, parameters.Count);
        Assert.Equal(agentId, parameters.Get<AgentId>("agent_id"));
        Assert.Equal("SELECT * FROM users", parameters.Get<string>("input.query"));
        Assert.Equal(30, parameters.Get<int>("input.timeout"));
        Assert.NotNull(parameters.Get<object>("retry_policy"));

        // Assert metadata
        Assert.Equal(7, metadata.Count);
        Assert.Equal("ETL", metadata.Get<string>("category"));
        Assert.Equal(1, metadata.Get<int>("priority"));
        Assert.True(metadata.Get<bool>("requires_auth"));
    }

    [Fact]
    public void ShouldImmutabilityCheck_WhenUsingComplexScenario()
    {
        // Arrange
        var agentId = AgentId.Create();
        var original = FlowStepParameters.CreateBuilder()
            .AddAgent(agentId)
            .AddLoop(5)
            .Build();

        // Act - Multiple modifications
        var modified1 = original.Set("agent_id", AgentId2);
        var modified2 = modified1.Set("max_iterations", 10);
        var modified3 = modified2.Set("new_param", "value");

        // Assert - Each instance is independent
        Assert.Equal(2, original.Count);
        Assert.Equal(agentId, original.Get<AgentId>("agent_id"));
        Assert.Equal(5, original.Get<int>("max_iterations"));

        Assert.Equal(2, modified1.Count);
        Assert.Equal(AgentId2, modified1.Get<string>("agent_id"));
        Assert.Equal(5, modified1.Get<int>("max_iterations"));

        Assert.Equal(2, modified2.Count);
        Assert.Equal(AgentId2, modified2.Get<string>("agent_id"));
        Assert.Equal(10, modified2.Get<int>("max_iterations"));

        Assert.Equal(3, modified3.Count);
        Assert.Equal(AgentId2, modified3.Get<string>("agent_id"));
        Assert.Equal(10, modified3.Get<int>("max_iterations"));
        Assert.Equal("value", modified3.Get<string>("new_param"));
    }

    [Fact]
    public void ShouldConditionalFlowStep_WhenUsingComplexScenario()
    {
        // Simulate a conditional flow step
        var baseParameters = FlowStepParameters.CreateBuilder()
            .AddAgent(AgentId.Create())
            .AddCondition("previousStep.result.success == true")
            .Build();

        // Branch 1: Success path
        var successPath = baseParameters
            .Set("tool", "SuccessHandler")
            .Set("next_step", "finalize");

        // Branch 2: Failure path
        var failurePath = baseParameters
            .Set("tool", "ErrorHandler")
            .Set("next_step", "retry")
            .Set("max_iterations", 3);

        // Assert base
        Assert.Equal(2, baseParameters.Count);
        Assert.False(baseParameters.ContainsKey("tool"));

        // Assert success path
        Assert.Equal(4, successPath.Count);
        Assert.Equal("SuccessHandler", successPath.Get<string>("tool"));
        Assert.Equal("finalize", successPath.Get<string>("next_step"));

        // Assert failure path
        Assert.Equal(5, failurePath.Count);
        Assert.Equal("ErrorHandler", failurePath.Get<string>("tool"));
        Assert.Equal("retry", failurePath.Get<string>("next_step"));
        Assert.Equal(3, failurePath.Get<int>("max_iterations"));
    }

    [Fact]
    public void ShouldLoopingFlowStep_WhenUsingComplexScenario()
    {
        // Create a flow step that processes items in batches
        var loopParameters = FlowStepParameters.CreateBuilder()
            .AddAgent(AgentId.Create())
            .AddTask(TaskId.Create())
            .AddLoop(100) // Process up to 100 batches
            .AddCondition("hasMoreData && currentBatch < maxBatches")
            .AddInput("batchSize", 50)
            .AddInput("source", "queue")
            .Add("progress_tracking", true)
            .Build();

        // Create metadata for tracking
        var loopMetadata = FlowStepMetadata.CreateBuilder()
            .AddDescription("Processes data in batches of 50 items")
            .AddCategory("batch-processing")
            .AddPriority(2)
            .AddTags(new List<string> { "loop", "batch", "queue" })
            .Add("average_batch_time", "30s")
            .Add("total_estimated_time", "50m")
            .Build();

        // Verify loop configuration
        Assert.Equal(100, loopParameters.Get<int>("max_iterations"));
        Assert.Equal(50, loopParameters.Get<int>("input.batchSize"));
        Assert.Equal("queue", loopParameters.Get<string>("input.source"));
        Assert.True(loopParameters.Get<bool>("progress_tracking"));

        // Verify metadata
        Assert.Contains("loop", loopMetadata.Get<List<string>>("tags") ?? []);
        Assert.Equal("30s", loopMetadata.Get<string>("average_batch_time"));
    }

    #endregion
}
