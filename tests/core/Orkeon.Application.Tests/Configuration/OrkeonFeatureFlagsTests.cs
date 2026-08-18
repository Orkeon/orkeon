using Orkeon.Application.Configuration;
using Orkeon.Application.Constants.Execution;

namespace Orkeon.Application.Tests.Configuration;

/// <summary>
/// SONAR-14: pins the three feature-flag presets — the defaults, the development
/// profile (experimental features on, telemetry off) and the hardened production
/// profile (advanced features off, wider concurrency and timeout).
/// </summary>
public class OrkeonFeatureFlagsTests
{
    [Fact]
    public void TheDefaultPreset_EnablesTheCoreToggles_AndKeepsAdvancedFeaturesOff()
    {
        var flags = OrkeonFeatureFlags.Default;

        Assert.True(flags.EnableEvents);
        Assert.True(flags.EnableTelemetry);
        Assert.True(flags.EnableMemoryPersistence);
        Assert.True(flags.EnableToolValidation);
        Assert.True(flags.EnableAsyncExecution);
        Assert.True(flags.EnablePerformanceMetrics);

        Assert.False(flags.EnableAdvancedDelegation);
        Assert.False(flags.EnableHumanInTheLoop);
        Assert.False(flags.EnableKnowledgeAugmentation);
        Assert.False(flags.EnableExperimentalFeatures);

        Assert.Equal(10, flags.MaxConcurrentOperations);
        Assert.Equal(ExecutionDefaults.DefaultMaxExecutionSeconds, flags.DefaultTimeoutSeconds);
    }

    [Fact]
    public void TheDevelopmentPreset_TurnsExperimentalFeaturesOn_AndTelemetryOff()
    {
        var flags = OrkeonFeatureFlags.Development;

        Assert.False(flags.EnableTelemetry);
        Assert.True(flags.EnableExperimentalFeatures);
        Assert.True(flags.EnableAdvancedDelegation);
        Assert.True(flags.EnableHumanInTheLoop);
        Assert.True(flags.EnableKnowledgeAugmentation);

        // Unspecified toggles keep their record defaults.
        Assert.True(flags.EnableEvents);
        Assert.Equal(10, flags.MaxConcurrentOperations);
    }

    [Fact]
    public void TheProductionPreset_HardensTheToggles_AndWidensTheLimits()
    {
        var flags = OrkeonFeatureFlags.Production;

        Assert.True(flags.EnableTelemetry);
        Assert.False(flags.EnableExperimentalFeatures);
        Assert.False(flags.EnableAdvancedDelegation);
        Assert.False(flags.EnableHumanInTheLoop);
        Assert.False(flags.EnableKnowledgeAugmentation);
        Assert.Equal(50, flags.MaxConcurrentOperations);
        Assert.Equal(600, flags.DefaultTimeoutSeconds);
    }

    [Fact]
    public void ThePresets_AreRecords_SoWithMutationsKeepTheRest()
    {
        var custom = OrkeonFeatureFlags.Default with { MaxConcurrentOperations = 3 };

        Assert.Equal(3, custom.MaxConcurrentOperations);
        Assert.True(custom.EnableEvents);
        Assert.NotEqual(OrkeonFeatureFlags.Default, custom);
        Assert.Equal(OrkeonFeatureFlags.Default, OrkeonFeatureFlags.Default with { });
    }
}
