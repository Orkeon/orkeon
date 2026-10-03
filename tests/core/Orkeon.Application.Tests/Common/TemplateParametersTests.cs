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
