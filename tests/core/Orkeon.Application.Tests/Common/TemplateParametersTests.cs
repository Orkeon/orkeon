using Orkeon.Application.Common;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Application.Tests.Common;

public class TemplateParametersTests
{
    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingTemplateInstantiationParametersWithEmpty()
    {
        // Act
        var parameters = TemplateInstantiationParameters.Empty;

        // Assert
        Assert.NotNull(parameters);
        Assert.Equal(0, parameters.Count);
        Assert.Empty(parameters.Keys);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingTemplateInstantiationParametersGettingWithExistingKey()
    {
        // Arrange
        var parameters = TemplateInstantiationParameters.CreateBuilder()
            .AddName("Agent Smith")
            .AddRole(RoleAnalyst)
            .AddMaxIterations(10)
            .Build();

        // Act
        var name = parameters.Get<string>("name");
        var role = parameters.Get<string>("role");
        var iterations = parameters.Get<int>("maxIterations");

        // Assert
        Assert.Equal("Agent Smith", name);
        Assert.Equal(RoleAnalyst, role);
        Assert.Equal(10, iterations);
    }

    [Fact]
    public void ShouldReturnDefault_WhenUsingTemplateInstantiationParametersGettingWithNonExistingKey()
    {
        // Arrange
        var parameters = TemplateInstantiationParameters.Empty;

        // Act
        var stringValue = parameters.Get<string>("nonexistent");
        var intValue = parameters.Get<int>("missing");
        var boolValue = parameters.Get<bool>("nothere");

        // Assert
        Assert.Null(stringValue);
        Assert.Equal(0, intValue);
        Assert.False(boolValue);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingTemplateInstantiationParametersGettingRequiredWithExistingKey()
    {
        // Arrange
        var parameters = TemplateInstantiationParameters.CreateBuilder()
            .AddGoal("Complete the analysis")
            .Build();

        // Act
        var goal = parameters.GetRequired<string>("goal");

        // Assert
        Assert.Equal("Complete the analysis", goal);
    }

    [Fact]
    public void ShouldThrow_WhenUsingTemplateInstantiationParametersGettingRequiredWithNonExistingKey()
    {
        // Arrange
        var parameters = TemplateInstantiationParameters.Empty;

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => parameters.GetRequired<string>("missing"));
        Assert.Contains("Required template parameter 'missing' not found", ex.Message);
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingTemplateInstantiationParametersUsingContains()
    {
        // Arrange
        var parameters = TemplateInstantiationParameters.CreateBuilder()
            .AddBackstory("Experienced analyst")
            .Build();

        // Act & Assert
        Assert.True(parameters.Contains("backstory"));
        Assert.False(parameters.Contains("nonexistent"));
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingTemplateInstantiationParametersUsingBuilder()
    {
        // Arrange & Act
        var parameters = TemplateInstantiationParameters.CreateBuilder()
            .AddName("TestAgent")
            .AddRole("Researcher")
            .AddGoal("Find information")
            .AddBackstory("Expert researcher")
            .AddDescription("Detailed description")
            .AddExpectedOutput("Research report")
            .AddTools(["search", "analyze"])
            .AddMaxIterations(5)
            .AddTimeout(TimeSpan.FromMinutes(30))
            .Add("customKey", "customValue")
            .Build();

        // Assert
        Assert.Equal(10, parameters.Count);
        Assert.Equal("TestAgent", parameters.Get<string>("name"));
        Assert.Equal("Researcher", parameters.Get<string>("role"));
        Assert.Equal(2, parameters.Get<IReadOnlyList<string>>("tools")?.Count);
        Assert.Equal(TimeSpan.FromMinutes(30), parameters.Get<TimeSpan>("timeout"));
        Assert.Equal("customValue", parameters.Get<string>("customKey"));
    }

    [Fact]
    public void ShouldCreateCorrectly_WhenUsingTemplateInstantiationParametersFromDictionary()
    {
        // Arrange
        var dictionary = new Dictionary<string, object>
        {
            ["name"] = "Agent",
            ["iterations"] = 10,
            ["enabled"] = true,
            ["tags"] = new List<string> { "tag1", "tag2" }
        };

        // Act
        var parameters = TemplateInstantiationParameters.FromDictionary(dictionary);

        // Assert
        Assert.Equal(4, parameters.Count);
        Assert.Equal("Agent", parameters.Get<string>("name"));
        Assert.Equal(10, parameters.Get<int>("iterations"));
        Assert.True(parameters.Get<bool>("enabled"));
        Assert.Equal(2, parameters.Get<IReadOnlyList<string>>("tags")?.Count);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingTemplateInstantiationParametersFromDictionaryWithNullOrEmpty()
    {
        // Act
        var nullParams = TemplateInstantiationParameters.FromDictionary(null!);
        var emptyParams = TemplateInstantiationParameters.FromDictionary(new Dictionary<string, object>());

        // Assert
        Assert.Equal(0, nullParams.Count);
        Assert.Equal(0, emptyParams.Count);
    }

    [Fact]
    public void ShouldConvertCorrectly_WhenUsingTemplateInstantiationParametersToDictionary()
    {
        // Arrange
        var parameters = TemplateInstantiationParameters.CreateBuilder()
            .AddName("Test")
            .AddMaxIterations(5)
            .Build();

        // Act
        var dictionary = parameters.ToDictionary();

        // Assert
        Assert.Equal(2, dictionary.Count);
        Assert.Equal("Test", dictionary["name"]);
        Assert.Equal(5, dictionary["maxIterations"]);
    }

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingInvalidTemplateParametersWithEmpty()
    {
        // Act
        var invalid = InvalidTemplateParameters.Empty;

        // Assert
        Assert.NotNull(invalid);
        Assert.Equal(0, invalid.Count);
        Assert.Empty(invalid.Parameters);
        Assert.Empty(invalid.Errors);
    }

    [Fact]
    public void ShouldReturnCorrectError_WhenUsingInvalidTemplateParametersGettingError()
    {
        // Arrange
        var invalid = InvalidTemplateParameters.CreateBuilder()
            .AddError("name", "Name is required")
            .AddError("age", "Age must be positive")
            .Build();

        // Act
        var nameError = invalid.GetError("name");
        var ageError = invalid.GetError("age");
        var missingError = invalid.GetError("missing");

        // Assert
        Assert.Equal("Name is required", nameError);
        Assert.Equal("Age must be positive", ageError);
        Assert.Null(missingError);
    }

    [Fact]
    public void ShouldReturnCorrectResult_WhenUsingInvalidTemplateParametersUsingHasError()
    {
        // Arrange
        var invalid = InvalidTemplateParameters.CreateBuilder()
            .AddError("field1", "Error 1")
            .Build();

        // Act & Assert
        Assert.True(invalid.HasError("field1"));
        Assert.False(invalid.HasError("field2"));
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingInvalidTemplateParametersUsingBuilderWithVariousErrors()
    {
        // Arrange & Act
        var invalid = InvalidTemplateParameters.CreateBuilder()
            .AddError("generic", "Generic error")
            .AddTypeError("age", typeof(int), typeof(string))
            .AddMissingError("requiredField")
            .AddRangeError("score", 0, 100, 150)
            .Build();

        // Assert
        Assert.Equal(4, invalid.Count);
        Assert.Contains("Generic error", invalid.GetError("generic"));
        Assert.Contains("Expected type Int32 but got String", invalid.GetError("age"));
        Assert.Contains("Required parameter 'requiredField' is missing", invalid.GetError("requiredField"));
        Assert.Contains("Value 150 is outside the allowed range [0, 100]", invalid.GetError("score"));
    }

    [Fact]
    public void ShouldCreateCorrectly_WhenUsingInvalidTemplateParametersFromDictionary()
    {
        // Arrange
        var errors = new Dictionary<string, string>
        {
            ["field1"] = "Error 1",
            ["field2"] = "Error 2"
        };

        // Act
        var invalid = InvalidTemplateParameters.FromDictionary(errors);

        // Assert
        Assert.Equal(2, invalid.Count);
        Assert.Equal("Error 1", invalid.GetError("field1"));
        Assert.Equal("Error 2", invalid.GetError("field2"));
    }

    [Fact]
    public void ShouldConvertCorrectly_WhenUsingInvalidTemplateParametersToDictionary()
    {
        // Arrange
        var invalid = InvalidTemplateParameters.CreateBuilder()
            .AddError("error1", "Message 1")
            .AddError("error2", "Message 2")
            .Build();

        // Act
        var dictionary = invalid.ToDictionary();

        // Assert
        Assert.Equal(2, dictionary.Count);
        Assert.Equal("Message 1", dictionary["error1"]);
        Assert.Equal("Message 2", dictionary["error2"]);
    }

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingResolvedTemplateParametersWithEmpty()
    {
        // Act
        var resolved = ResolvedTemplateParameters.Empty;

        // Assert
        Assert.NotNull(resolved);
        var dict = resolved.ToDictionary();
        Assert.Empty(dict);
    }

    [Fact]
    public void ShouldReturnCorrectValues_WhenUsingResolvedTemplateParametersGetting()
    {
        // Arrange
        var resolved = ResolvedTemplateParameters.CreateBuilder()
            .Add("resolved1", "value1")
            .Add("resolved2", 42)
            .Build();

        // Act
        var string1 = resolved.Get<string>("resolved1");
        var int1 = resolved.Get<int>("resolved2");
        var missing = resolved.Get<string>("missing");

        // Assert
        Assert.Equal("value1", string1);
        Assert.Equal(42, int1);
        Assert.Null(missing);
    }

    [Fact]
    public void ShouldCopyCorrectly_WhenUsingResolvedTemplateParametersFromInstantiationParameters()
    {
        // Arrange
        var original = TemplateInstantiationParameters.CreateBuilder()
            .AddName("Original")
            .AddMaxIterations(5)
            .Build();

        var additional = new Dictionary<string, object>
        {
            ["resolvedId"] = "12345",
            ["timestamp"] = DateTime.UtcNow
        };

        // Act
        var resolved = ResolvedTemplateParameters.FromInstantiationParameters(original, additional);

        // Assert
        var dict = resolved.ToDictionary();
        Assert.Equal(4, dict.Count); // 2 from original + 2 additional
        Assert.Equal("Original", resolved.Get<string>("name"));
        Assert.Equal(5, resolved.Get<int>("maxIterations"));
        Assert.Equal("12345", resolved.Get<string>("resolvedId"));
        Assert.True(resolved.Get<DateTime>("timestamp") != default);
    }

    [Fact]
    public void ShouldReturnEmptyInstance_WhenUsingTemplateConfigurationWithEmpty()
    {
        // Act
        var config = TemplateConfiguration.Empty;

        // Assert
        Assert.NotNull(config);
        var dict = config.ToDictionary();
        Assert.Empty(dict);
    }

    [Fact]
    public void ShouldBuildCorrectly_WhenUsingTemplateConfigurationUsingBuilder()
    {
        // Arrange & Act
        var config = TemplateConfiguration.CreateBuilder()
            .AddTemplate("template-123")
            .AddVersion("1.0.0")
            .AddEnvironment("production")
            .Add("feature", "enabled")
            .Build();

        // Assert
        Assert.Equal("template-123", config.Get<string>("templateId"));
        Assert.Equal("1.0.0", config.Get<string>("version"));
        Assert.Equal("production", config.Get<string>("environment"));
        Assert.Equal("enabled", config.Get<string>("feature"));
    }

    [Fact]
    public void ShouldCreateCorrectly_WhenUsingTemplateConfigurationFromDictionary()
    {
        // Arrange
        var dictionary = new Dictionary<string, object>
        {
            ["setting1"] = "value1",
            ["setting2"] = 100,
            ["setting3"] = true
        };

        // Act
        var config = TemplateConfiguration.FromDictionary(dictionary);

        // Assert
        Assert.Equal("value1", config.Get<string>("setting1"));
        Assert.Equal(100, config.Get<int>("setting2"));
        Assert.True(config.Get<bool>("setting3"));
    }

    [Fact]
    public void ShouldReturnEmpty_WhenUsingTemplateConfigurationFromDictionaryWithNullOrEmpty()
    {
        // Act
        var nullConfig = TemplateConfiguration.FromDictionary(null!);
        var emptyConfig = TemplateConfiguration.FromDictionary([]);

        // Assert
        Assert.Empty(nullConfig.ToDictionary());
        Assert.Empty(emptyConfig.ToDictionary());
    }

    [Fact]
    public void ShouldWrapValue_WhenUsingTemplateParameterValueFrom()
    {
        // Act
        var stringValue = TemplateParameterValue.From("test");
        var intValue = TemplateParameterValue.From(42);
        var listValue = TemplateParameterValue.From(new List<int> { 1, 2, 3 });

        // Assert
        Assert.Equal("test", stringValue.RawValue);
        Assert.Equal(typeof(string), stringValue.ValueType);
        Assert.Equal(42, intValue.RawValue);
        Assert.Equal(typeof(int), intValue.ValueType);
        Assert.IsType<List<int>>(listValue.RawValue);
    }

    [Fact]
    public void ShouldThrow_WhenUsingTemplateParameterValueFromWithNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => TemplateParameterValue.From(null!));
    }

    [Fact]
    public void ShouldReturn_WhenUsingTemplateParameterValueGettingValueWithCorrectType()
    {
        // Arrange
        var value = TemplateParameterValue.From("test string");

        // Act
        var result = value.GetValue<string>();

        // Assert
        Assert.Equal("test string", result);
    }

    [Fact]
    public void ShouldConvert_WhenUsingTemplateParameterValueGettingValueWithConvertibleType()
    {
        // Arrange
        var intValue = TemplateParameterValue.From(42);
        var stringValue = TemplateParameterValue.From("123");

        // Act
        var asDouble = intValue.GetValue<double>();
        var asInt = stringValue.GetValue<int>();

        // Assert
        Assert.Equal(42.0, asDouble);
        Assert.Equal(123, asInt);
    }

    [Fact]
    public void ShouldThrow_WhenUsingTemplateParameterValueGettingValueWithIncompatibleType()
    {
        // Arrange
        var value = TemplateParameterValue.From("not a number");

        // Act & Assert
        var ex = Assert.Throws<InvalidCastException>(() => value.GetValue<int>());
        Assert.Contains("Cannot convert template parameter value", ex.Message);
    }

    [Fact]
    public void ShouldFullTemplateWorkflow_WhenUsingComplexScenario()
    {
        // Stage 1: Create instantiation parameters
        var instantiationParams = TemplateInstantiationParameters.CreateBuilder()
            .AddName("DataAnalyst")
            .AddRole("Senior Data Analyst")
            .AddGoal("Analyze customer behavior patterns")
            .AddBackstory("10 years of experience in data science")
            .AddTools(["pandas", "scikit-learn", "matplotlib"])
            .AddMaxIterations(10)
            .AddTimeout(TimeSpan.FromHours(2))
            .Build();

        // Stage 2: Validate parameters (simulate validation)
        var invalidParams = InvalidTemplateParameters.CreateBuilder();

        // Check required fields
        if (!instantiationParams.Contains("name"))
            invalidParams.AddMissingError("name");

        // Check type constraints
        var iterations = instantiationParams.Get<int>("maxIterations");
        if (iterations <= 0 || iterations > 100)
            invalidParams.AddRangeError("maxIterations", 1, 100, iterations);

        var errors = invalidParams.Build();
        Assert.Equal(0, errors.Count); // All valid

        // Stage 3: Create configuration
        var config = TemplateConfiguration.CreateBuilder()
            .AddTemplate("analyst-template-v2")
            .AddVersion("2.1.0")
            .AddEnvironment("production")
            .Add("llmProvider", ProviderOpenAI)
            .Add("temperature", 0.7)
            .Build();

        // Stage 4: Resolve parameters
        var resolvedParams = ResolvedTemplateParameters.FromInstantiationParameters(
            instantiationParams,
            new Dictionary<string, object>
            {
                ["agentId"] = Guid.NewGuid().ToString(),
                ["createdAt"] = DateTime.UtcNow,
                ["resolvedLlmModel"] = ModelGpt4
            });

        // Verify final state
        Assert.Equal("DataAnalyst", resolvedParams.Get<string>("name"));
        Assert.NotNull(resolvedParams.Get<string>("agentId"));
        Assert.Equal(ModelGpt4, resolvedParams.Get<string>("resolvedLlmModel"));

        // Convert for serialization
        var serializedParams = resolvedParams.ToDictionary();
        Assert.True(serializedParams.Count > instantiationParams.Count);
    }

    [Fact]
    public void ShouldEmptyCollections_WhenUsingEdgeCases()
    {
        // Test empty tools list
        var params1 = TemplateInstantiationParameters.CreateBuilder()
            .AddTools([])
            .Build();

        var tools = params1.Get<IReadOnlyList<string>>("tools");
        Assert.NotNull(tools);
        Assert.Empty(tools);

        // Test empty builder
        var params2 = TemplateInstantiationParameters.CreateBuilder().Build();
        Assert.Equal(0, params2.Count);

        // Test empty error builder
        var errors = InvalidTemplateParameters.CreateBuilder().Build();
        Assert.Equal(0, errors.Count);
    }

    [Fact]
    public void ShouldComplexScenarios_WhenUsingTypeConversion()
    {
        // Test with TimeSpan
        var timeParams = TemplateInstantiationParameters.CreateBuilder()
            .Add("duration", TimeSpan.FromMinutes(30))
            .Build();

        var duration = timeParams.Get<TimeSpan>("duration");
        Assert.Equal(TimeSpan.FromMinutes(30), duration);

        // Test with nested object
        var nestedData = new Dictionary<string, object>
        {
            ["level1"] = new Dictionary<string, object>
            {
                ["level2"] = "value"
            }
        };

        var nestedParams = TemplateInstantiationParameters.CreateBuilder()
            .Add("nested", nestedData)
            .Build();

        var retrieved = nestedParams.Get<Dictionary<string, object>>("nested");
        Assert.NotNull(retrieved);
        Assert.True(retrieved.ContainsKey("level1"));
    }
}
