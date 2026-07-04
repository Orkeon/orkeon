using Orkeon.Domain.Crew.Planning;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Domain.Tests.Planning;

/// <summary>
/// Tests for PlanningConfiguration following Clean Architecture principles.
/// Tests configuration settings for AI-powered planning capabilities.
/// </summary>
public class PlanningConfigurationTests
{
    #region Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaultValues_WhenUsingDefaultConstructor()
    {
        // Act
        var config = new PlanningConfiguration();

        // Assert
        Assert.True(config.EnablePlanning);
        Assert.Equal(ModelGpt4oMini, config.PlanningLlmModel);
        Assert.Equal(0.1, config.Temperature);
    }

    #endregion

    #region Property Tests

    [Fact]
    public void ShouldBeSettable_WhenEnablingPlanning()
    {
        // Act
        var configDisabled = new PlanningConfiguration { EnablePlanning = false };
        var configEnabled = new PlanningConfiguration { EnablePlanning = true };

        // Assert
        Assert.False(configDisabled.EnablePlanning);
        Assert.True(configEnabled.EnablePlanning);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingPlanningLlmModel()
    {
        // Act
        var config = new PlanningConfiguration { PlanningLlmModel = ModelGpt4 };

        // Assert
        Assert.Equal(ModelGpt4, config.PlanningLlmModel);
    }

    [Theory]
    [InlineData(ModelGpt4oMini)]
    [InlineData(ModelGpt4)]
    [InlineData(ModelGpt35Turbo)]
    [InlineData("claude-3-opus")]
    [InlineData("llama-3-70b")]
    [InlineData("")]
    [InlineData("custom-model-v1")]
    public void ShouldAcceptVariousModels_WhenUsingPlanningLlmModel(string model)
    {
        // Act
        var config = new PlanningConfiguration { PlanningLlmModel = model };

        // Assert
        Assert.Equal(model, config.PlanningLlmModel);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingTemperature()
    {
        // Act
        var config = new PlanningConfiguration { Temperature = 0.7 };

        // Assert
        Assert.Equal(0.7, config.Temperature);
    }

    [Theory]
    [InlineData(0.0)]      // Minimum valid temperature
    [InlineData(0.1)]      // Default temperature
    [InlineData(0.5)]      // Medium temperature
    [InlineData(0.9)]      // High temperature
    [InlineData(1.0)]      // Maximum typical temperature
    [InlineData(2.0)]      // Above typical range
    public void ShouldAcceptVariousValues_WhenUsingTemperature(double temperature)
    {
        // Act
        var config = new PlanningConfiguration { Temperature = temperature };

        // Assert
        Assert.Equal(temperature, config.Temperature);
    }

    #endregion

    #region Configuration Scenarios

    [Fact]
    public void ShouldScenario_WhenUsingConfigurationDisabledPlanning()
    {
        // Arrange & Act
        var config = new PlanningConfiguration
        {
            EnablePlanning = false,
            PlanningLlmModel = null!,  // Model not needed when disabled
            Temperature = 0.0
        };

        // Assert
        Assert.False(config.EnablePlanning);
        Assert.Null(config.PlanningLlmModel);
        Assert.Equal(0.0, config.Temperature);
    }

    [Fact]
    public void ShouldScenario_WhenUsingConfigurationCreativeWriting()
    {
        // Arrange & Act - High temperature for creative tasks
        var config = new PlanningConfiguration
        {
            EnablePlanning = true,
            PlanningLlmModel = ModelGpt4,
            Temperature = 0.9
        };

        // Assert
        Assert.True(config.EnablePlanning);
        Assert.Equal(ModelGpt4, config.PlanningLlmModel);
        Assert.Equal(0.9, config.Temperature);
    }

    [Fact]
    public void ShouldScenario_WhenUsingConfigurationPrecisePlanning()
    {
        // Arrange & Act - Low temperature for precise planning
        var config = new PlanningConfiguration
        {
            EnablePlanning = true,
            PlanningLlmModel = ModelGpt4oMini,
            Temperature = 0.0
        };

        // Assert
        Assert.True(config.EnablePlanning);
        Assert.Equal(ModelGpt4oMini, config.PlanningLlmModel);
        Assert.Equal(0.0, config.Temperature);
    }

    [Fact]
    public void ShouldScenario_WhenUsingConfigurationLocalModel()
    {
        // Arrange & Act - Configuration for local/custom models
        var config = new PlanningConfiguration
        {
            EnablePlanning = true,
            PlanningLlmModel = "ollama:mixtral",
            Temperature = 0.3
        };

        // Assert
        Assert.True(config.EnablePlanning);
        Assert.Equal("ollama:mixtral", config.PlanningLlmModel);
        Assert.Equal(0.3, config.Temperature);
    }

    #endregion

    #region Edge Cases and Validation

    [Fact]
    public void ShouldBeAccepted_WhenUsingTemperatureWithNegativeValue()
    {
        // Act
        var config = new PlanningConfiguration { Temperature = -0.5 };

        // Assert
        Assert.Equal(-0.5, config.Temperature);
    }

    [Fact]
    public void ShouldBeAccepted_WhenUsingTemperatureWithNaN()
    {
        // Act
        var config = new PlanningConfiguration { Temperature = double.NaN };

        // Assert
        Assert.True(double.IsNaN(config.Temperature));
    }

    [Fact]
    public void ShouldBeAccepted_WhenUsingTemperatureWithInfinity()
    {
        // Act
        var configPos = new PlanningConfiguration { Temperature = double.PositiveInfinity };
        var configNeg = new PlanningConfiguration { Temperature = double.NegativeInfinity };

        // Assert
        Assert.True(double.IsPositiveInfinity(configPos.Temperature));
        Assert.True(double.IsNegativeInfinity(configNeg.Temperature));
    }

    [Fact]
    public void ShouldBeAccepted_WhenUsingPlanningLlmModelWithNull()
    {
        // Act
        var config = new PlanningConfiguration { PlanningLlmModel = null! };

        // Assert
        Assert.Null(config.PlanningLlmModel);
    }

    [Fact]
    public void ShouldBeAccepted_WhenUsingPlanningLlmModelWithUnicodeCharacters()
    {
        // Act
        var config = new PlanningConfiguration { PlanningLlmModel = "模型-中文-🤖" };

        // Assert
        Assert.Equal("模型-中文-🤖", config.PlanningLlmModel);
    }

    [Fact]
    public void ShouldBeAccepted_WhenUsingPlanningLlmModelWithVeryLongName()
    {
        // Arrange
        var longModelName = new string('a', 1000) + "-model-v1";

        // Act
        var config = new PlanningConfiguration { PlanningLlmModel = longModelName };

        // Assert
        Assert.Equal(longModelName, config.PlanningLlmModel);
    }

    #endregion

    #region Multiple Configuration Instances

    [Fact]
    public void ShouldCreateDistinctInstances_WhenUsingConfigurationWithDifferentValues()
    {
        // Act
        var config1 = new PlanningConfiguration
        {
            EnablePlanning = false,
            PlanningLlmModel = "model-v1",
            Temperature = 0.5
        };

        var config2 = new PlanningConfiguration
        {
            EnablePlanning = true,
            PlanningLlmModel = "model-v2",
            Temperature = 0.8
        };

        var config3 = new PlanningConfiguration { Temperature = 0.2 };

        // Assert
        Assert.False(config1.EnablePlanning);
        Assert.Equal("model-v1", config1.PlanningLlmModel);
        Assert.Equal(0.5, config1.Temperature);

        Assert.True(config2.EnablePlanning);
        Assert.Equal("model-v2", config2.PlanningLlmModel);
        Assert.Equal(0.8, config2.Temperature);

        // config3 should use defaults for unset props
        Assert.True(config3.EnablePlanning);
        Assert.Equal(ModelGpt4oMini, config3.PlanningLlmModel);
        Assert.Equal(0.2, config3.Temperature);
    }

    #endregion

    #region ToString and Object Methods

    [Fact]
    public void ShouldReturnNonNullString_WhenCallingToString()
    {
        // Arrange
        var config = new PlanningConfiguration();

        // Act
        var result = config.ToString();

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void ShouldReturnConsistentValue_WhenCallingGetHashCode()
    {
        // Arrange
        var config = new PlanningConfiguration
        {
            EnablePlanning = true,
            PlanningLlmModel = ModelGpt4,
            Temperature = 0.7
        };

        // Act
        var hash1 = config.GetHashCode();
        var hash2 = config.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameReference()
    {
        // Arrange
        var config = new PlanningConfiguration();

        // Act & Assert
        Assert.True(config.Equals(config));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithNull()
    {
        // Arrange
        var config = new PlanningConfiguration();

        // Act & Assert
        Assert.False(config.Equals(null));
    }

    #endregion

    #region Common Planning Configurations

    [Fact]
    public void ShouldBeValid_WhenUsingCommonConfigurations()
    {
        // Arrange & Act - Different common configurations
        var configs = new[]
        {
            new PlanningConfiguration // Default
            {
                EnablePlanning = true,
                PlanningLlmModel = ModelGpt4oMini,
                Temperature = 0.1
            },
            new PlanningConfiguration // High performance
            {
                EnablePlanning = true,
                PlanningLlmModel = ModelGpt4,
                Temperature = 0.0
            },
            new PlanningConfiguration // Balanced
            {
                EnablePlanning = true,
                PlanningLlmModel = ModelGpt35Turbo,
                Temperature = 0.5
            },
            new PlanningConfiguration // Creative
            {
                EnablePlanning = true,
                PlanningLlmModel = "claude-3-opus",
                Temperature = 0.8
            },
            new PlanningConfiguration // Disabled
            {
                EnablePlanning = false,
                PlanningLlmModel = "",
                Temperature = 0.0
            }
        };

        // Assert
        Assert.All(configs, config =>
        {
            Assert.NotNull(config);
            Assert.InRange(config.Temperature, -10.0, 10.0);
            Assert.NotNull(config.PlanningLlmModel);
        });
    }

    #endregion
}
