using Orkeon.Application.Configuration;

namespace Orkeon.Domain.Tests.Configuration;

public class OrkeonFeatureFlagsTests
{
    #region Constructor and Default Values Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingOrkeonFeatureFlagsWithDefaultConstructor()
    {
        var flags = new OrkeonFeatureFlags();
        Assert.True(flags.EnableEvents);
        Assert.True(flags.EnableTelemetry);
        Assert.False(flags.EnableAdvancedDelegation);
        Assert.True(flags.EnableMemoryPersistence);
        Assert.True(flags.EnableToolValidation);
        Assert.True(flags.EnableAsyncExecution);
        Assert.False(flags.EnableHumanInTheLoop);
        Assert.False(flags.EnableKnowledgeAugmentation);
        Assert.True(flags.EnablePerformanceMetrics);
        Assert.False(flags.EnableExperimentalFeatures);
        Assert.Equal(10, flags.MaxConcurrentOperations);
        Assert.Equal(300, flags.DefaultTimeoutSeconds);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingOrkeonFeatureFlagsProperties()
    {
        var flags = new OrkeonFeatureFlags
        {
            EnableEvents = false,
            EnableTelemetry = false,
            EnableAdvancedDelegation = true,
            EnableMemoryPersistence = false,
            EnableToolValidation = false,
            EnableAsyncExecution = false,
            EnableHumanInTheLoop = true,
            EnableKnowledgeAugmentation = true,
            EnablePerformanceMetrics = false,
            EnableExperimentalFeatures = true,
            MaxConcurrentOperations = 50,
            DefaultTimeoutSeconds = 600
        };

        Assert.False(flags.EnableEvents);
        Assert.False(flags.EnableTelemetry);
        Assert.True(flags.EnableAdvancedDelegation);
        Assert.False(flags.EnableMemoryPersistence);
        Assert.False(flags.EnableToolValidation);
        Assert.False(flags.EnableAsyncExecution);
        Assert.True(flags.EnableHumanInTheLoop);
        Assert.True(flags.EnableKnowledgeAugmentation);
        Assert.False(flags.EnablePerformanceMetrics);
        Assert.True(flags.EnableExperimentalFeatures);
        Assert.Equal(50, flags.MaxConcurrentOperations);
        Assert.Equal(600, flags.DefaultTimeoutSeconds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldAcceptBothValues_WhenUsingOrkeonFeatureFlagsBooleanProperties(bool value)
    {
        var flags = new OrkeonFeatureFlags
        {
            EnableEvents = value,
            EnableTelemetry = value,
            EnableAdvancedDelegation = value,
            EnableMemoryPersistence = value,
            EnableToolValidation = value,
            EnableAsyncExecution = value,
            EnableHumanInTheLoop = value,
            EnableKnowledgeAugmentation = value,
            EnablePerformanceMetrics = value,
            EnableExperimentalFeatures = value
        };
        Assert.Equal(value, flags.EnableEvents);
        Assert.Equal(value, flags.EnableTelemetry);
        Assert.Equal(value, flags.EnableAdvancedDelegation);
        Assert.Equal(value, flags.EnableMemoryPersistence);
        Assert.Equal(value, flags.EnableToolValidation);
        Assert.Equal(value, flags.EnableAsyncExecution);
        Assert.Equal(value, flags.EnableHumanInTheLoop);
        Assert.Equal(value, flags.EnableKnowledgeAugmentation);
        Assert.Equal(value, flags.EnablePerformanceMetrics);
        Assert.Equal(value, flags.EnableExperimentalFeatures);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(1000)]
    public void ShouldAcceptVariousValues_WhenUsingOrkeonFeatureFlagsWithMaxConcurrentOperations(int maxOperations)
    {
        var flags = new OrkeonFeatureFlags { MaxConcurrentOperations = maxOperations };
        Assert.Equal(maxOperations, flags.MaxConcurrentOperations);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(300)]
    [InlineData(600)]
    [InlineData(3600)]
    public void ShouldAcceptVariousValues_WhenUsingOrkeonFeatureFlagsWithDefaultTimeoutSeconds(int timeoutSeconds)
    {
        var flags = new OrkeonFeatureFlags { DefaultTimeoutSeconds = timeoutSeconds };
        Assert.Equal(timeoutSeconds, flags.DefaultTimeoutSeconds);
    }

    #endregion

    #region Static Factory Methods Tests

    [Fact]
    public void ShouldReturnDefaultConfiguration_WhenUsingOrkeonFeatureFlagsWithDefault()
    {
        var flags = OrkeonFeatureFlags.Default;
        Assert.NotNull(flags);
        Assert.True(flags.EnableEvents);
        Assert.True(flags.EnableTelemetry);
        Assert.False(flags.EnableAdvancedDelegation);
        Assert.Equal(10, flags.MaxConcurrentOperations);
        Assert.Equal(300, flags.DefaultTimeoutSeconds);
    }

    [Fact]
    public void ShouldReturnDevelopmentConfiguration_WhenUsingOrkeonFeatureFlagsDevelopment()
    {
        var flags = OrkeonFeatureFlags.Development;
        Assert.NotNull(flags);
        Assert.True(flags.EnableEvents);
        Assert.False(flags.EnableTelemetry);
        Assert.True(flags.EnableAdvancedDelegation);
        Assert.True(flags.EnableHumanInTheLoop);
        Assert.True(flags.EnableKnowledgeAugmentation);
        Assert.True(flags.EnableExperimentalFeatures);
        Assert.Equal(10, flags.MaxConcurrentOperations);
        Assert.Equal(300, flags.DefaultTimeoutSeconds);
    }

    [Fact]
    public void ShouldReturnProductionConfiguration_WhenUsingOrkeonFeatureFlagsProduction()
    {
        var flags = OrkeonFeatureFlags.Production;
        Assert.NotNull(flags);
        Assert.True(flags.EnableTelemetry);
        Assert.False(flags.EnableAdvancedDelegation);
        Assert.False(flags.EnableHumanInTheLoop);
        Assert.False(flags.EnableKnowledgeAugmentation);
        Assert.False(flags.EnableExperimentalFeatures);
        Assert.Equal(50, flags.MaxConcurrentOperations);
        Assert.Equal(600, flags.DefaultTimeoutSeconds);
    }

    [Fact]
    public void ShouldReturnNewInstances_WhenUsingOrkeonFeatureFlagsStaticFactories()
    {
        var default1 = OrkeonFeatureFlags.Default;
        var default2 = OrkeonFeatureFlags.Default;
        var dev1 = OrkeonFeatureFlags.Development;
        var dev2 = OrkeonFeatureFlags.Development;
        var prod1 = OrkeonFeatureFlags.Production;
        var prod2 = OrkeonFeatureFlags.Production;

        Assert.NotSame(default1, default2);
        Assert.NotSame(dev1, dev2);
        Assert.NotSame(prod1, prod2);
        Assert.NotSame(default1, dev1);
        Assert.NotSame(dev1, prod1);
    }

    #endregion

    #region Environment Configuration Comparison Tests

    [Fact]
    public void ShouldHaveCorrectDifferences_WhenUsingOrkeonFeatureFlagsDevelopmentVsProduction()
    {
        var development = OrkeonFeatureFlags.Development;
        var production = OrkeonFeatureFlags.Production;

        Assert.False(development.EnableTelemetry);
        Assert.True(production.EnableTelemetry);
        Assert.True(development.EnableAdvancedDelegation);
        Assert.False(production.EnableAdvancedDelegation);
        Assert.True(development.EnableHumanInTheLoop);
        Assert.False(production.EnableHumanInTheLoop);
        Assert.True(development.EnableKnowledgeAugmentation);
        Assert.False(production.EnableKnowledgeAugmentation);
        Assert.True(development.EnableExperimentalFeatures);
        Assert.False(production.EnableExperimentalFeatures);
        Assert.Equal(10, development.MaxConcurrentOperations);
        Assert.Equal(50, production.MaxConcurrentOperations);
        Assert.Equal(300, development.DefaultTimeoutSeconds);
        Assert.Equal(600, production.DefaultTimeoutSeconds);
    }

    [Fact]
    public void ShouldShowExpectedDifferences_WhenUsingOrkeonFeatureFlagsWithDefaultVsEnvironments()
    {
        var defaultFlags = OrkeonFeatureFlags.Default;
        var development = OrkeonFeatureFlags.Development;
        var production = OrkeonFeatureFlags.Production;

        Assert.Equal(defaultFlags.EnableEvents, development.EnableEvents);
        Assert.Equal(defaultFlags.EnableEvents, production.EnableEvents);
        Assert.Equal(defaultFlags.EnableMemoryPersistence, development.EnableMemoryPersistence);
        Assert.Equal(defaultFlags.EnableMemoryPersistence, production.EnableMemoryPersistence);
        Assert.NotEqual(defaultFlags.EnableTelemetry, development.EnableTelemetry);
        Assert.Equal(defaultFlags.EnableTelemetry, production.EnableTelemetry);
        Assert.NotEqual(defaultFlags.MaxConcurrentOperations, production.MaxConcurrentOperations);
        Assert.Equal(defaultFlags.MaxConcurrentOperations, development.MaxConcurrentOperations);
    }

    #endregion

    #region Feature Flag Combinations Tests

    [Fact]
    public void ShouldAllowBasicFunctionality_WhenUsingOrkeonFeatureFlagsCoreFeaturesEnabled()
    {
        var flags = new OrkeonFeatureFlags { EnableEvents = true, EnableMemoryPersistence = true, EnableToolValidation = true, EnableAsyncExecution = true };
        Assert.True(flags.EnableEvents && flags.EnableMemoryPersistence && flags.EnableToolValidation && flags.EnableAsyncExecution);
    }

    [Fact]
    public void ShouldEnableFullFunctionality_WhenUsingOrkeonFeatureFlagsAdvancedFeaturesEnabled()
    {
        var flags = new OrkeonFeatureFlags { EnableAdvancedDelegation = true, EnableHumanInTheLoop = true, EnableKnowledgeAugmentation = true, EnableExperimentalFeatures = true };
        Assert.True(flags.EnableAdvancedDelegation && flags.EnableHumanInTheLoop && flags.EnableKnowledgeAugmentation && flags.EnableExperimentalFeatures);
    }

    [Fact]
    public void ShouldEnableObservability_WhenUsingOrkeonFeatureFlagsMonitoringEnabled()
    {
        var flags = new OrkeonFeatureFlags { EnableTelemetry = true, EnablePerformanceMetrics = true };
        Assert.True(flags.EnableTelemetry && flags.EnablePerformanceMetrics);
    }

    [Fact]
    public void ShouldStillFunctionMinimally_WhenUsingOrkeonFeatureFlagsWithAllFeaturesDisabled()
    {
        var flags = new OrkeonFeatureFlags
        {
            EnableEvents = false,
            EnableTelemetry = false,
            EnableAdvancedDelegation = false,
            EnableMemoryPersistence = false,
            EnableToolValidation = false,
            EnableAsyncExecution = false,
            EnableHumanInTheLoop = false,
            EnableKnowledgeAugmentation = false,
            EnablePerformanceMetrics = false,
            EnableExperimentalFeatures = false
        };
        Assert.False(flags.EnableEvents);
        Assert.False(flags.EnableTelemetry);
        Assert.Equal(10, flags.MaxConcurrentOperations);
        Assert.Equal(300, flags.DefaultTimeoutSeconds);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ShouldAcceptValues_WhenUsingOrkeonFeatureFlagsWithZeroValues()
    {
        var flags = new OrkeonFeatureFlags { MaxConcurrentOperations = 0, DefaultTimeoutSeconds = 0 };
        Assert.Equal(0, flags.MaxConcurrentOperations);
        Assert.Equal(0, flags.DefaultTimeoutSeconds);
    }

    [Fact]
    public void ShouldAcceptValues_WhenUsingOrkeonFeatureFlagsWithNegativeValues()
    {
        var flags = new OrkeonFeatureFlags { MaxConcurrentOperations = -1, DefaultTimeoutSeconds = -100 };
        Assert.Equal(-1, flags.MaxConcurrentOperations);
        Assert.Equal(-100, flags.DefaultTimeoutSeconds);
    }

    [Fact]
    public void ShouldAcceptValues_WhenUsingOrkeonFeatureFlagsWithVeryLargeValues()
    {
        var flags = new OrkeonFeatureFlags { MaxConcurrentOperations = int.MaxValue, DefaultTimeoutSeconds = int.MaxValue };
        Assert.Equal(int.MaxValue, flags.MaxConcurrentOperations);
        Assert.Equal(int.MaxValue, flags.DefaultTimeoutSeconds);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingOrkeonFeatureFlagsToString()
    {
        var flags = new OrkeonFeatureFlags { MaxConcurrentOperations = 42 };
        var s = flags.ToString();
        Assert.NotNull(s);
        Assert.NotEmpty(s);
        Assert.Contains("OrkeonFeatureFlags", s);
    }

    #endregion

    #region Business Logic Tests

    [Fact]
    public void ShouldDisableRiskyFeatures_WhenUsingOrkeonFeatureFlagsProductionSafety()
    {
        var production = OrkeonFeatureFlags.Production;
        Assert.False(production.EnableAdvancedDelegation);
        Assert.False(production.EnableHumanInTheLoop);
        Assert.False(production.EnableExperimentalFeatures);
        Assert.True(production.EnableTelemetry);
        Assert.True(production.EnableToolValidation);
    }

    [Fact]
    public void ShouldEnableTestingFeatures_WhenUsingOrkeonFeatureFlagsDevelopmentFlexibility()
    {
        var development = OrkeonFeatureFlags.Development;
        Assert.True(development.EnableAdvancedDelegation);
        Assert.True(development.EnableHumanInTheLoop);
        Assert.True(development.EnableExperimentalFeatures);
        Assert.False(development.EnableTelemetry);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 300)]
    [InlineData(50, 600)]
    [InlineData(100, 3600)]
    public void ShouldScaleAppropriately_WhenUsingOrkeonFeatureFlagsPerformanceSettings(int maxOperations, int expectedMinTimeoutSeconds)
    {
        var flags = new OrkeonFeatureFlags { MaxConcurrentOperations = maxOperations, DefaultTimeoutSeconds = expectedMinTimeoutSeconds };
        if (maxOperations > 20) Assert.True(flags.DefaultTimeoutSeconds >= 600);
        Assert.Equal(maxOperations, flags.MaxConcurrentOperations);
        Assert.Equal(expectedMinTimeoutSeconds, flags.DefaultTimeoutSeconds);
    }

    [Fact]
    public void ShouldMaintainLogicalSettings_WhenUsingOrkeonFeatureFlagsEnvironmentConsistency()
    {
        var development = OrkeonFeatureFlags.Development;
        var production = OrkeonFeatureFlags.Production;
        Assert.True(development.EnableEvents);
        Assert.True(production.EnableEvents);
        Assert.True(development.EnableMemoryPersistence);
        Assert.True(production.EnableMemoryPersistence);
        Assert.True(production.MaxConcurrentOperations > development.MaxConcurrentOperations);
        Assert.True(production.DefaultTimeoutSeconds > development.DefaultTimeoutSeconds);
    }

    #endregion
}
