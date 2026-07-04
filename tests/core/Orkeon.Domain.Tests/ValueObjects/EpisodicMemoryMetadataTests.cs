using Orkeon.Domain.Memory.ValueObjects;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

public class EpisodicMemoryMetadataTests
{
    private static readonly string[] Tools12 = ["tool1", "tool2"];
    private static readonly string[] ToolsCased = ["Tool1", "Tool2", "Tool3"];
    private static readonly string[] Agents12Cased = ["Agent1", "Agent2"];
    private static readonly string[] Agents12 = ["agent1", "agent2"];
    private static readonly string[] FileReaderWebScraperCalculator = ["FileReader", "WebScraper", "Calculator"];
    private static readonly string[] Agents123Cased = ["Agent1", "Agent2", "Agent3"];
    private static readonly string[] Agents123 = ["agent1", "agent2", "agent3"];
    private static readonly string[] AbcArray = ["a", "b", "c"];
    private static readonly string[] Item1Item2Item3 = ["item1", "item2", "item3"];
    private static readonly int[] Int123 = [1, 2, 3];

    #region EpisodicMemoryMetadata Tests

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEmpty()
    {
        // Act
        var metadata = EpisodicMemoryMetadata.Empty;

        // Assert
        Assert.NotNull(metadata);
        Assert.Empty(metadata.Keys);
        Assert.False(metadata.ContainsKey("anyKey"));
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingWithExistingKey()
    {
        // Arrange
        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddContext("test context")
            .AddSuccessScore(0.85)
            .AddIterationCount(5)
            .Build();

        // Act & Assert
        Assert.Equal("test context", metadata.Get<string>("context"));
        Assert.Equal(0.85, metadata.Get<double>("success_score"));
        Assert.Equal(5, metadata.Get<int>("iteration_count"));
    }

    [Fact]
    public void ShouldReturnDefault_WhenGettingWithNonExistentKey()
    {
        // Arrange
        var metadata = EpisodicMemoryMetadata.Empty;

        // Act & Assert
        Assert.Null(metadata.Get<string>("missing"));
        Assert.Equal(0, metadata.Get<int>("missing"));
        Assert.Equal(0.0, metadata.Get<double>("missing"));
        Assert.False(metadata.Get<bool>("missing"));
    }

    [Fact]
    public void ShouldReturnValue_WhenGettingRequiredWithExistingKey()
    {
        // Arrange
        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddContext("required context")
            .Build();

        // Act
        var result = metadata.GetRequired<string>("context");

        // Assert
        Assert.Equal("required context", result);
    }

    [Fact]
    public void ShouldThrowKeyNotFoundException_WhenGettingRequiredWithNonExistentKey()
    {
        // Arrange
        var metadata = EpisodicMemoryMetadata.Empty;

        // Act & Assert
        var exception = Assert.Throws<KeyNotFoundException>(
            () => metadata.GetRequired<string>("missing"));
        Assert.Contains("Required metadata key 'missing' not found", exception.Message);
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingContainsKey()
    {
        // Arrange
        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddContext("test")
            .Build();

        // Act & Assert
        Assert.True(metadata.ContainsKey("context"));
        Assert.False(metadata.ContainsKey("missing"));
    }

    [Fact]
    public void ShouldReturnAllKeys_WhenUsingKeys()
    {
        // Arrange
        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddContext("test")
            .AddSuccessScore(0.9)
            .AddEnvironment("production")
            .Build();

        // Act
        var keys = metadata.Keys.ToList();

        // Assert
        Assert.Equal(3, keys.Count);
        Assert.Contains("context", keys);
        Assert.Contains("success_score", keys);
        Assert.Contains("environment", keys);
    }

    [Fact]
    public void ShouldReturnAllValues_WhenUsingToDictionary()
    {
        // Arrange
        var tools = Tools12;
        var collaborators = Agents12;

        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddContext("test context")
            .AddToolsUsed(tools)
            .AddCollaborators(collaborators)
            .AddSuccessScore(0.75)
            .AddEnvironment("dev")
            .AddIterationCount(3)
            .Build();

        // Act
        var dict = metadata.ToDictionary();

        // Assert
        Assert.Equal(6, dict.Count);
        Assert.Equal("test context", dict["context"]);
        Assert.Equal(tools, dict["tools_used"]);
        Assert.Equal(collaborators, dict["collaborators"]);
        Assert.Equal(0.75, dict["success_score"]);
        Assert.Equal("dev", dict["environment"]);
        Assert.Equal(3, dict["iteration_count"]);
    }

    [Fact]
    public void ShouldAddContextMetadata_WhenUsingBuilderAddContext()
    {
        // Act
        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddContext("execution context")
            .Build();

        // Assert
        Assert.Equal("execution context", metadata.Get<string>("context"));
    }

    [Fact]
    public void ShouldAddToolsMetadata_WhenUsingBuilderAddToolsUsed()
    {
        // Arrange
        var tools = FileReaderWebScraperCalculator;

        // Act
        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddToolsUsed(tools)
            .Build();

        // Assert
        var result = metadata.Get<string[]>("tools_used");
        Assert.Equal(tools, result);
    }

    [Fact]
    public void ShouldAddCollaboratorsMetadata_WhenUsingBuilderAddCollaborators()
    {
        // Arrange
        var collaborators = Agents123Cased;

        // Act
        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddCollaborators(collaborators)
            .Build();

        // Assert
        var result = metadata.Get<string[]>("collaborators");
        Assert.Equal(collaborators, result);
    }

    [Fact]
    public void ShouldAddScoreMetadata_WhenUsingBuilderAddSuccessScore()
    {
        // Act
        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddSuccessScore(0.95)
            .Build();

        // Assert
        Assert.Equal(0.95, metadata.Get<double>("success_score"));
    }

    [Fact]
    public void ShouldAddEnvironmentMetadata_WhenUsingBuilderAddEnvironment()
    {
        // Act
        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddEnvironment("production")
            .Build();

        // Assert
        Assert.Equal("production", metadata.Get<string>("environment"));
    }

    [Fact]
    public void ShouldAddCountMetadata_WhenUsingBuilderAddIterationCount()
    {
        // Act
        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddIterationCount(10)
            .Build();

        // Assert
        Assert.Equal(10, metadata.Get<int>("iteration_count"));
    }

    [Fact]
    public void ShouldAddCustomMetadata_WhenUsingBuilderAdd()
    {
        // Act
        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .Add("custom_string", "value")
            .Add("custom_int", 42)
            .Add("custom_bool", true)
            .Add("custom_double", 3.14)
            .Build();

        // Assert
        Assert.Equal("value", metadata.Get<string>("custom_string"));
        Assert.Equal(42, metadata.Get<int>("custom_int"));
        Assert.True(metadata.Get<bool>("custom_bool"));
        Assert.Equal(3.14, metadata.Get<double>("custom_double"));
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithNull()
    {
        // Act
        var metadata = EpisodicMemoryMetadata.FromDictionary(null);

        // Assert
        Assert.Same(EpisodicMemoryMetadata.Empty, metadata);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingFromDictionaryWithEmptyDictionary()
    {
        // Act
        var metadata = EpisodicMemoryMetadata.FromDictionary(new Dictionary<string, object>());

        // Assert
        Assert.Same(EpisodicMemoryMetadata.Empty, metadata);
    }

    [Fact]
    public void ShouldCreateMetadata_WhenUsingFromDictionaryWithValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "context", "test context" },
            { "success_score", 0.85 },
            { "iteration_count", 5 },
            { "tools_used", Tools12 }
        };

        // Act
        var metadata = EpisodicMemoryMetadata.FromDictionary(dict);

        // Assert
        Assert.Equal("test context", metadata.Get<string>("context"));
        Assert.Equal(0.85, metadata.Get<double>("success_score"));
        Assert.Equal(5, metadata.Get<int>("iteration_count"));
        Assert.Equal(Tools12, metadata.Get<string[]>("tools_used"));
    }

    [Fact]
    public void ShouldAddMultipleMetadata_WhenUsingBuilderChainedCalls()
    {
        // Arrange
        var tools = Tools12;
        var collaborators = Agents123;

        // Act
        var metadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddContext("complex execution")
            .AddToolsUsed(tools)
            .AddCollaborators(collaborators)
            .AddSuccessScore(0.92)
            .AddEnvironment("staging")
            .AddIterationCount(7)
            .Add("custom_flag", true)
            .Build();

        // Assert
        Assert.Equal(7, metadata.Keys.Count());
        Assert.Equal("complex execution", metadata.Get<string>("context"));
        Assert.Equal(tools, metadata.Get<string[]>("tools_used"));
        Assert.Equal(collaborators, metadata.Get<string[]>("collaborators"));
        Assert.Equal(0.92, metadata.Get<double>("success_score"));
        Assert.Equal("staging", metadata.Get<string>("environment"));
        Assert.Equal(7, metadata.Get<int>("iteration_count"));
        Assert.True(metadata.Get<bool>("custom_flag"));
    }

    #endregion

    #region EpisodeEventData Tests

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingEpisodeEventDataWithEmpty()
    {
        // Act
        var eventData = EpisodeEventData.Empty;

        // Assert
        Assert.NotNull(eventData);
        var dict = eventData.ToDictionary();
        Assert.Empty(dict);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingEpisodeEventDataGettingWithExistingKey()
    {
        // Arrange
        var eventData = EpisodeEventData.CreateBuilder()
            .AddInput("test input")
            .AddOutput("test output")
            .AddDuration(TimeSpan.FromSeconds(5))
            .Build();

        // Act & Assert
        Assert.Equal("test input", eventData.Get<string>("input"));
        Assert.Equal("test output", eventData.Get<string>("output"));
        Assert.Equal(5000.0, eventData.Get<double>("duration_ms"));
    }

    [Fact]
    public void ShouldReturnDefault_WhenUsingEpisodeEventDataGettingWithNonExistentKey()
    {
        // Arrange
        var eventData = EpisodeEventData.Empty;

        // Act & Assert
        Assert.Null(eventData.Get<string>("missing"));
        Assert.Equal(0, eventData.Get<int>("missing"));
        Assert.Equal(0.0, eventData.Get<double>("missing"));
    }

    [Fact]
    public void ShouldAddInputData_WhenUsingEpisodeEventDataUsingBuilderAddInput()
    {
        // Act
        var eventData = EpisodeEventData.CreateBuilder()
            .AddInput("user query")
            .Build();

        // Assert
        Assert.Equal("user query", eventData.Get<string>("input"));
    }

    [Fact]
    public void ShouldAddOutputData_WhenUsingEpisodeEventDataUsingBuilderAddOutput()
    {
        // Act
        var eventData = EpisodeEventData.CreateBuilder()
            .AddOutput("agent response")
            .Build();

        // Assert
        Assert.Equal("agent response", eventData.Get<string>("output"));
    }

    [Fact]
    public void ShouldAddDurationInMilliseconds_WhenUsingEpisodeEventDataUsingBuilderAddDuration()
    {
        // Arrange
        var duration = TimeSpan.FromMinutes(2.5);

        // Act
        var eventData = EpisodeEventData.CreateBuilder()
            .AddDuration(duration)
            .Build();

        // Assert
        Assert.Equal(150000.0, eventData.Get<double>("duration_ms")); // 2.5 minutes = 150000 ms
    }

    [Fact]
    public void ShouldAddErrorData_WhenUsingEpisodeEventDataUsingBuilderAddError()
    {
        // Act
        var eventData = EpisodeEventData.CreateBuilder()
            .AddError("Connection timeout")
            .Build();

        // Assert
        Assert.Equal("Connection timeout", eventData.Get<string>("error"));
    }

    [Fact]
    public void ShouldAddToolData_WhenUsingEpisodeEventDataUsingBuilderAddToolName()
    {
        // Act
        var eventData = EpisodeEventData.CreateBuilder()
            .AddToolName("WebScraper")
            .Build();

        // Assert
        Assert.Equal("WebScraper", eventData.Get<string>("tool_name"));
    }

    [Fact]
    public void ShouldAddRoleData_WhenUsingEpisodeEventDataUsingBuilderAddAgentRole()
    {
        // Act
        var eventData = EpisodeEventData.CreateBuilder()
            .AddAgentRole("Researcher")
            .Build();

        // Assert
        Assert.Equal("Researcher", eventData.Get<string>("agent_role"));
    }

    [Fact]
    public void ShouldAddConfidenceData_WhenUsingEpisodeEventDataUsingBuilderAddConfidence()
    {
        // Act
        var eventData = EpisodeEventData.CreateBuilder()
            .AddConfidence(0.87)
            .Build();

        // Assert
        Assert.Equal(0.87, eventData.Get<double>("confidence"));
    }

    [Fact]
    public void ShouldAddCustomData_WhenUsingEpisodeEventDataUsingBuilderAdd()
    {
        // Act
        var eventData = EpisodeEventData.CreateBuilder()
            .Add("custom_field", "custom value")
            .Add("custom_number", 123)
            .Build();

        // Assert
        Assert.Equal("custom value", eventData.Get<string>("custom_field"));
        Assert.Equal(123, eventData.Get<int>("custom_number"));
    }

    [Fact]
    public void ShouldReturnAllData_WhenUsingEpisodeEventDataToDictionary()
    {
        // Arrange
        var eventData = EpisodeEventData.CreateBuilder()
            .AddInput("input data")
            .AddOutput("output data")
            .AddDuration(TimeSpan.FromSeconds(10))
            .AddToolName("Calculator")
            .AddAgentRole(RoleAnalyst)
            .AddConfidence(0.95)
            .Build();

        // Act
        var dict = eventData.ToDictionary();

        // Assert
        Assert.Equal(6, dict.Count);
        Assert.Equal("input data", dict["input"]);
        Assert.Equal("output data", dict["output"]);
        Assert.Equal(10000.0, dict["duration_ms"]);
        Assert.Equal("Calculator", dict["tool_name"]);
        Assert.Equal(RoleAnalyst, dict["agent_role"]);
        Assert.Equal(0.95, dict["confidence"]);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingEpisodeEventDataFromDictionaryWithNull()
    {
        // Act
        var eventData = EpisodeEventData.FromDictionary(null);

        // Assert
        Assert.Same(EpisodeEventData.Empty, eventData);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingEpisodeEventDataFromDictionaryWithEmptyDictionary()
    {
        // Act
        var eventData = EpisodeEventData.FromDictionary(new Dictionary<string, object>());

        // Assert
        Assert.Same(EpisodeEventData.Empty, eventData);
    }

    [Fact]
    public void ShouldCreateEventData_WhenUsingEpisodeEventDataFromDictionaryWithValues()
    {
        // Arrange
        var dict = new Dictionary<string, object>
        {
            { "input", "test input" },
            { "output", "test output" },
            { "duration_ms", 5000.0 },
            { "tool_name", "TestTool" }
        };

        // Act
        var eventData = EpisodeEventData.FromDictionary(dict);

        // Assert
        Assert.Equal("test input", eventData.Get<string>("input"));
        Assert.Equal("test output", eventData.Get<string>("output"));
        Assert.Equal(5000.0, eventData.Get<double>("duration_ms"));
        Assert.Equal("TestTool", eventData.Get<string>("tool_name"));
    }

    #endregion

    #region EpisodicMetadataValue Tests

    [Fact]
    public void ShouldCreate_WhenUsingEpisodicMetadataValueFromWithValidValue()
    {
        // Act
        var stringValue = EpisodicMetadataValue.From("test");
        var intValue = EpisodicMetadataValue.From(42);
        var boolValue = EpisodicMetadataValue.From(true);
        var doubleValue = EpisodicMetadataValue.From(3.14);
        var dateValue = EpisodicMetadataValue.From(DateTime.UtcNow);
        var arrayValue = EpisodicMetadataValue.From(AbcArray);

        // Assert
        Assert.NotNull(stringValue);
        Assert.NotNull(intValue);
        Assert.NotNull(boolValue);
        Assert.NotNull(doubleValue);
        Assert.NotNull(dateValue);
        Assert.NotNull(arrayValue);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingEpisodicMetadataValueFromWithNull()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => EpisodicMetadataValue.From(null!));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingEpisodicMetadataValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = EpisodicMetadataValue.From("test string");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("test string", result);
    }

    [Fact]
    public void ShouldConvert_WhenUsingEpisodicMetadataValueGettingValueWithConvertibleType()
    {
        // Arrange
        var intValue = EpisodicMetadataValue.From(123);

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
    public void ShouldThrowInvalidCastException_WhenUsingEpisodicMetadataValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = EpisodicMetadataValue.From("not a number");

        // Act & Assert
        var exception = Assert.Throws<InvalidCastException>(
            () => value.GetValue<int>());
        Assert.Contains("Cannot convert episodic metadata value", exception.Message);
    }

    [Fact]
    public void ShouldReturnOriginalValue_WhenUsingEpisodicMetadataValueUsingRawValue()
    {
        // Arrange
        var original = new { Name = "Test", Value = 123 };
        var value = EpisodicMetadataValue.From(original);

        // Act
        var raw = value.RawValue;

        // Assert
        Assert.Same(original, raw);
    }

    [Fact]
    public void ShouldReturnCorrectType_WhenUsingEpisodicMetadataValueUsingValueType()
    {
        // Arrange
        var stringValue = EpisodicMetadataValue.From("test");
        var intValue = EpisodicMetadataValue.From(42);
        var boolValue = EpisodicMetadataValue.From(true);
        var arrayValue = EpisodicMetadataValue.From(Int123);

        // Act & Assert
        Assert.Equal(typeof(string), stringValue.ValueType);
        Assert.Equal(typeof(int), intValue.ValueType);
        Assert.Equal(typeof(bool), boolValue.ValueType);
        Assert.Equal(typeof(int[]), arrayValue.ValueType);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingEpisodicMetadataValueWithArrayType()
    {
        // Arrange
        var stringArray = Item1Item2Item3;
        var value = EpisodicMetadataValue.From(stringArray);

        // Act
        var retrieved = value.GetValue<string[]>();

        // Assert
        Assert.Equal(stringArray, retrieved);
    }

    #endregion

    #region Complex Scenarios

    [Fact]
    public void ShouldPreserveData_WhenUsingComplexScenarioUsingRoundTripConversion()
    {
        // Arrange
        var originalMetadata = EpisodicMemoryMetadata.CreateBuilder()
            .AddContext("complex scenario")
            .AddToolsUsed(ToolsCased)
            .AddCollaborators(Agents12Cased)
            .AddSuccessScore(0.88)
            .AddEnvironment("test")
            .AddIterationCount(15)
            .Add("custom_data", new { type = "test", value = 123 })
            .Build();

        // Act
        var dict = originalMetadata.ToDictionary();
        var reconstructed = EpisodicMemoryMetadata.FromDictionary(dict);

        // Assert
        Assert.Equal("complex scenario", reconstructed.Get<string>("context"));
        Assert.Equal(ToolsCased, reconstructed.Get<string[]>("tools_used"));
        Assert.Equal(Agents12Cased, reconstructed.Get<string[]>("collaborators"));
        Assert.Equal(0.88, reconstructed.Get<double>("success_score"));
        Assert.Equal("test", reconstructed.Get<string>("environment"));
        Assert.Equal(15, reconstructed.Get<int>("iteration_count"));
        // Note: Complex object will be stored as its raw value
        Assert.NotNull(reconstructed.Get<object>("custom_data"));
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingComplexScenarioUsingEventDataWithError()
    {
        // Arrange & Act
        var eventData = EpisodeEventData.CreateBuilder()
            .AddInput("process data")
            .AddError("Process failed: timeout")
            .AddDuration(TimeoutQuick)
            .AddToolName("DataProcessor")
            .AddAgentRole("Processor")
            .AddConfidence(0.0) // Zero confidence due to error
            .Build();

        // Assert
        Assert.Equal("process data", eventData.Get<string>("input"));
        Assert.Equal("Process failed: timeout", eventData.Get<string>("error"));
        Assert.Null(eventData.Get<string>("output")); // No output due to error
        Assert.Equal(30000.0, eventData.Get<double>("duration_ms"));
        Assert.Equal("DataProcessor", eventData.Get<string>("tool_name"));
        Assert.Equal(0.0, eventData.Get<double>("confidence"));
    }

    #endregion
}
