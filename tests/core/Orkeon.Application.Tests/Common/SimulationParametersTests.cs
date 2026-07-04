using Orkeon.Application.Crew;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Common;

public class SimulationParametersTests
{
    [Fact]
    public void ShouldUseDefaultValues_WhenConstructingWithDefaults()
    {
        // Act
        var parameters = new SimulationParameters();

        // Assert
        Assert.Equal(10, parameters.Iterations);
        Assert.Equal(0.8, parameters.SuccessThreshold);
        Assert.Null(parameters.MaxDuration);
        Assert.True(parameters.EnableLogging);
        Assert.True(parameters.CollectMetrics);
        Assert.Equal(42, parameters.RandomSeed);
    }

    [Fact]
    public void ShouldSetAllValues_WhenConstructingWithAllParameters()
    {
        // Arrange
        var iterations = 25;
        var successThreshold = 0.95;
        var maxDuration = TimeSpan.FromMinutes(30);
        var enableLogging = false;
        var collectMetrics = false;
        var randomSeed = 12345;

        // Act
        var parameters = new SimulationParameters(
            iterations,
            successThreshold,
            maxDuration,
            enableLogging,
            collectMetrics,
            randomSeed);

        // Assert
        Assert.Equal(25, parameters.Iterations);
        Assert.Equal(0.95, parameters.SuccessThreshold);
        Assert.Equal(TimeSpan.FromMinutes(30), parameters.MaxDuration);
        Assert.False(parameters.EnableLogging);
        Assert.False(parameters.CollectMetrics);
        Assert.Equal(12345, parameters.RandomSeed);
    }

    [Fact]
    public void ShouldMixSpecifiedAndDefaults_WhenConstructingWithPartialParameters()
    {
        // Act - Only Iterations
        var params1 = new SimulationParameters(Iterations: 20);
        Assert.Equal(20, params1.Iterations);
        Assert.Equal(0.8, params1.SuccessThreshold);
        Assert.True(params1.EnableLogging);

        // Act - Only SuccessThreshold
        var params2 = new SimulationParameters(SuccessThreshold: 0.9);
        Assert.Equal(10, params2.Iterations);
        Assert.Equal(0.9, params2.SuccessThreshold);
        Assert.True(params2.CollectMetrics);

        // Act - Multiple parameters
        var params3 = new SimulationParameters(
            Iterations: 5,
            EnableLogging: false,
            RandomSeed: 999);
        Assert.Equal(5, params3.Iterations);
        Assert.False(params3.EnableLogging);
        Assert.Equal(999, params3.RandomSeed);
        Assert.Equal(0.8, params3.SuccessThreshold); // Default
    }

    [Fact]
    public void ShouldReturnDefaultParameters_WhenUsingDefault()
    {
        // Act
        var parameters = SimulationParameters.Default;

        // Assert
        Assert.NotNull(parameters);
        Assert.Equal(10, parameters.Iterations);
        Assert.Equal(0.8, parameters.SuccessThreshold);
        Assert.Null(parameters.MaxDuration);
        Assert.True(parameters.EnableLogging);
        Assert.True(parameters.CollectMetrics);
        Assert.Equal(42, parameters.RandomSeed);
    }

    [Fact]
    public void ShouldReturnQuickParameters_WhenUsingQuick()
    {
        // Act
        var parameters = SimulationParameters.Quick;

        // Assert
        Assert.NotNull(parameters);
        Assert.Equal(3, parameters.Iterations);
        Assert.False(parameters.EnableLogging);
        // Other values should be defaults
        Assert.Equal(0.8, parameters.SuccessThreshold);
        Assert.Null(parameters.MaxDuration);
        Assert.True(parameters.CollectMetrics);
        Assert.Equal(42, parameters.RandomSeed);
    }

    [Fact]
    public void ShouldReturnThoroughParameters_WhenUsingThorough()
    {
        // Act
        var parameters = SimulationParameters.Thorough;

        // Assert
        Assert.NotNull(parameters);
        Assert.Equal(50, parameters.Iterations);
        Assert.True(parameters.CollectMetrics);
        // Other values should be defaults
        Assert.Equal(0.8, parameters.SuccessThreshold);
        Assert.Null(parameters.MaxDuration);
        Assert.True(parameters.EnableLogging);
        Assert.Equal(42, parameters.RandomSeed);
    }

    [Fact]
    public void ShouldBeEqual_WhenRecordingEqualityWithSameValues()
    {
        // Arrange
        var duration = TimeoutLong;
        var params1 = new SimulationParameters(
            15, 0.85, duration, false, true, 123);
        var params2 = new SimulationParameters(
            15, 0.85, duration, false, true, 123);

        // Act & Assert
        Assert.Equal(params1, params2);
        Assert.True(params1 == params2);
        Assert.False(params1 != params2);
        Assert.Equal(params1.GetHashCode(), params2.GetHashCode());
    }

    [Fact]
    public void ShouldNotBeEqual_WhenRecordingEqualityWithDifferentValues()
    {
        // Arrange
        var baseParams = new SimulationParameters();

        // Act & Assert - Different Iterations
        var differentIterations = new SimulationParameters(Iterations: 20);
        Assert.NotEqual(baseParams, differentIterations);

        // Different SuccessThreshold
        var differentThreshold = new SimulationParameters(SuccessThreshold: 0.9);
        Assert.NotEqual(baseParams, differentThreshold);

        // Different MaxDuration
        var differentDuration = new SimulationParameters(MaxDuration: TimeoutExtended);
        Assert.NotEqual(baseParams, differentDuration);

        // Different EnableLogging
        var differentLogging = new SimulationParameters(EnableLogging: false);
        Assert.NotEqual(baseParams, differentLogging);

        // Different CollectMetrics
        var differentMetrics = new SimulationParameters(CollectMetrics: false);
        Assert.NotEqual(baseParams, differentMetrics);

        // Different RandomSeed
        var differentSeed = new SimulationParameters(RandomSeed: 999);
        Assert.NotEqual(baseParams, differentSeed);
    }

    [Fact]
    public void ShouldCreateModifiedCopy_WhenUsingWithSyntax()
    {
        // Arrange
        var original = new SimulationParameters(
            10, 0.8, TimeSpan.FromMinutes(20), true, true, 42);

        // Act
        var modified = original with
        {
            Iterations = 20,
            SuccessThreshold = 0.9,
            EnableLogging = false
        };

        // Assert
        Assert.NotEqual(original, modified);
        Assert.Equal(10, original.Iterations);
        Assert.Equal(20, modified.Iterations);
        Assert.Equal(0.8, original.SuccessThreshold);
        Assert.Equal(0.9, modified.SuccessThreshold);
        Assert.True(original.EnableLogging);
        Assert.False(modified.EnableLogging);
        // Unchanged values
        Assert.Equal(original.MaxDuration, modified.MaxDuration);
        Assert.Equal(original.CollectMetrics, modified.CollectMetrics);
        Assert.Equal(original.RandomSeed, modified.RandomSeed);
    }

    [Fact]
    public void ShouldWork_WhenUsingDeconstruction()
    {
        // Arrange
        var duration = TimeSpan.FromMinutes(45);
        var parameters = new SimulationParameters(
            25, 0.85, duration, false, true, 567);

        // Act
        var (iterations, successThreshold, maxDuration, enableLogging, collectMetrics, randomSeed) = parameters;

        // Assert
        Assert.Equal(25, iterations);
        Assert.Equal(0.85, successThreshold);
        Assert.Equal(duration, maxDuration);
        Assert.False(enableLogging);
        Assert.True(collectMetrics);
        Assert.Equal(567, randomSeed);
    }

    [Fact]
    public void ShouldIncludeAllProperties_WhenCallingToString()
    {
        // Arrange
        var parameters = new SimulationParameters(
            15,
            0.9,
            TimeSpan.FromMinutes(30),
            false,
            true,
            999);

        // Act
        var result = parameters.ToString();

        // Assert
        Assert.Contains("15", result);        // Iterations
        Assert.Contains("0.9", result);       // SuccessThreshold
        Assert.Contains("30", result);        // Duration (minutes)
        Assert.Contains("False", result);     // EnableLogging
        Assert.Contains("True", result);      // CollectMetrics
        Assert.Contains("999", result);       // RandomSeed
    }

    [Fact]
    public void ShouldAccept_WhenUsingBoundaryValues()
    {
        // Act & Assert - Zero iterations
        var zeroIterations = new SimulationParameters(Iterations: 0);
        Assert.Equal(0, zeroIterations.Iterations);

        // Negative iterations (might be used for special cases)
        var negativeIterations = new SimulationParameters(Iterations: -1);
        Assert.Equal(-1, negativeIterations.Iterations);

        // Success threshold at boundaries
        var minThreshold = new SimulationParameters(SuccessThreshold: 0.0);
        Assert.Equal(0.0, minThreshold.SuccessThreshold);

        var maxThreshold = new SimulationParameters(SuccessThreshold: 1.0);
        Assert.Equal(1.0, maxThreshold.SuccessThreshold);

        // Very large random seed
        var largeSeed = new SimulationParameters(RandomSeed: int.MaxValue);
        Assert.Equal(int.MaxValue, largeSeed.RandomSeed);
    }

    [Fact]
    public void ShouldSimulationProfiles_WhenUsingComplexScenario()
    {
        // Arrange - Different simulation profiles for different use cases
        var developmentParams = new SimulationParameters(
            Iterations: 5,
            SuccessThreshold: 0.7,
            MaxDuration: TimeoutStandard,
            EnableLogging: true,
            CollectMetrics: false,
            RandomSeed: 12345);

        var productionParams = new SimulationParameters(
            Iterations: 100,
            SuccessThreshold: 0.95,
            MaxDuration: TimeSpan.FromHours(2),
            EnableLogging: false,
            CollectMetrics: true,
            RandomSeed: DateTime.UtcNow.Millisecond);

        var testingParams = SimulationParameters.Quick with
        {
            CollectMetrics = false,
            RandomSeed = 0  // Fixed seed for reproducible tests
        };

        // Act & Assert - Verify different profiles
        // Development - balanced for debugging
        Assert.Equal(5, developmentParams.Iterations);
        Assert.True(developmentParams.EnableLogging);
        Assert.False(developmentParams.CollectMetrics);

        // Production - thorough with metrics
        Assert.Equal(100, productionParams.Iterations);
        Assert.False(productionParams.EnableLogging);
        Assert.True(productionParams.CollectMetrics);

        // Testing - quick and reproducible
        Assert.Equal(3, testingParams.Iterations);
        Assert.False(testingParams.EnableLogging);
        Assert.False(testingParams.CollectMetrics);
        Assert.Equal(0, testingParams.RandomSeed);
    }

    [Fact]
    public void ShouldReturnSameInstance_WhenUsingDefaultWithMultipleAccesses()
    {
        // Act
        var default1 = SimulationParameters.Default;
        var default2 = SimulationParameters.Default;

        // Assert - Should be the same instance
        Assert.Same(default1, default2);
    }

    [Fact]
    public void ShouldReturnSameInstance_WhenUsingQuickWithMultipleAccesses()
    {
        // Act
        var quick1 = SimulationParameters.Quick;
        var quick2 = SimulationParameters.Quick;

        // Assert - Should be the same instance
        Assert.Same(quick1, quick2);
    }

    [Fact]
    public void ShouldReturnSameInstance_WhenUsingThoroughWithMultipleAccesses()
    {
        // Act
        var thorough1 = SimulationParameters.Thorough;
        var thorough2 = SimulationParameters.Thorough;

        // Assert - Should be the same instance
        Assert.Same(thorough1, thorough2);
    }
}
