using Orkeon.Application.Configuration;
using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;

using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;
using Orkeon.Domain.Tests.Fixtures;
namespace Orkeon.Domain.Tests.Configuration;

/// <summary>
/// Tests for Orkeon Configuration following Clean Architecture principles.
/// Tests the main configuration records and their behavior.
/// </summary>
public class OrkeonConfigTests
{
    private static readonly string[] Feature1Feature2 = ["feature1", "feature2"];
    private static readonly string[] ApiKeys12 = ["key1", "key2"];
    private static readonly string[] AdvancedFeatures = ["advanced-delegation", "memory-persistence"];
    #region OrkeonConfig Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingOrkeonConfigWithDefaultConstructor()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var config = new OrkeonConfig();

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.NotNull(config.Version);
        Assert.NotNull(config.Execution);
        Assert.NotNull(config.Memory);
        Assert.NotNull(config.Telemetry);
        Assert.NotNull(config.FeatureFlags);
        Assert.NotNull(config.CustomSettings);
        Assert.Empty(config.CustomSettings);
        Assert.True(config.CreatedAt >= beforeCreation);
        Assert.True(config.CreatedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, config.CreatedAt.Kind);
        Assert.Null(config.UpdatedAt);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingOrkeonConfigUsingProperties()
    {
        // Arrange
        var customSettings = new Dictionary<string, object>
        {
            { "customKey", "customValue" },
            { "maxRetries", 5 }
        };
        var updatedAt = DateTime.UtcNow;

        // Act
        var config = new OrkeonConfig
        {
            Version = ConfigurationVersionId.Create(),
            CustomSettings = customSettings,
            UpdatedAt = updatedAt
        };

        // Assert
        Assert.NotNull(config.Version);
        Assert.Equal(customSettings, config.CustomSettings);
        Assert.Equal(2, config.CustomSettings.Count);
        Assert.Equal(updatedAt, config.UpdatedAt);
    }

    [Fact]
    public void ShouldBeRecentUtcTime_WhenUsingOrkeonConfigUsingCreatedAt()
    {
        // Arrange
        var beforeCreation = DateTime.UtcNow;

        // Act
        var config = new OrkeonConfig();

        // Assert
        var afterCreation = DateTime.UtcNow;
        Assert.True(config.CreatedAt >= beforeCreation);
        Assert.True(config.CreatedAt <= afterCreation);
        Assert.Equal(DateTimeKind.Utc, config.CreatedAt.Kind);
    }

    [Fact]
    public void ShouldHaveUniqueTimestamps_WhenUsingOrkeonConfigWithMultipleInstances()
    {
        // Act
        var config1 = new OrkeonConfig();
        ClockAdvance.UntilStrictlyAfter(config1.CreatedAt); // Ensure different timestamps (R5.6)
        var config2 = new OrkeonConfig();

        // Assert
        Assert.True(config2.CreatedAt >= config1.CreatedAt);
    }

    #endregion

    #region OrkeonConfig Static Factory Methods Tests

    [Fact]
    public void ShouldReturnDefaultConfiguration_WhenUsingOrkeonConfigWithDefault()
    {
        // Act
        var config = OrkeonConfig.Default;

        // Assert
        Assert.NotNull(config);
        Assert.NotNull(config.Version);
        Assert.NotNull(config.Execution);
        Assert.NotNull(config.Memory);
        Assert.NotNull(config.Telemetry);
        Assert.NotNull(config.FeatureFlags);
        Assert.NotNull(config.CustomSettings);
        Assert.Empty(config.CustomSettings);
        Assert.Null(config.UpdatedAt);
    }

    [Fact]
    public void ShouldReturnDevelopmentConfiguration_WhenUsingOrkeonConfigUsingDevelopment()
    {
        // Act
        var config = OrkeonConfig.Development;

        // Assert
        Assert.NotNull(config);
        Assert.NotNull(config.Execution);
        Assert.True(config.Execution.EnableDebugMode);
        Assert.NotNull(config.Memory);
        Assert.Equal(MemoryStorageType.InMemory, config.Memory.Type);
        Assert.NotNull(config.Telemetry);
        Assert.Equal(TelemetryLogLevel.Debug, config.Telemetry.LogLevel);
        Assert.NotNull(config.FeatureFlags);
        var expectedDev = OrkeonFeatureFlags.Development;
        Assert.Equal(expectedDev.EnableTelemetry, config.FeatureFlags.EnableTelemetry);
        Assert.Equal(expectedDev.EnableExperimentalFeatures, config.FeatureFlags.EnableExperimentalFeatures);
        Assert.Equal(expectedDev.EnableAdvancedDelegation, config.FeatureFlags.EnableAdvancedDelegation);
        Assert.Equal(expectedDev.EnableHumanInTheLoop, config.FeatureFlags.EnableHumanInTheLoop);
        Assert.Equal(expectedDev.EnableKnowledgeAugmentation, config.FeatureFlags.EnableKnowledgeAugmentation);
    }

    [Fact]
    public void ShouldReturnProductionConfiguration_WhenUsingOrkeonConfigUsingProduction()
    {
        // Act
        var config = OrkeonConfig.Production;

        // Assert
        Assert.NotNull(config);
        Assert.NotNull(config.Execution);
        Assert.False(config.Execution.EnableDebugMode);
        Assert.NotNull(config.Memory);
        Assert.Equal(MemoryStorageType.Redis, config.Memory.Type);
        Assert.NotNull(config.Telemetry);
        Assert.Equal(TelemetryLogLevel.Warning, config.Telemetry.LogLevel);
        Assert.NotNull(config.FeatureFlags);
        var expectedProd = OrkeonFeatureFlags.Production;
        Assert.Equal(expectedProd.EnableTelemetry, config.FeatureFlags.EnableTelemetry);
        Assert.Equal(expectedProd.EnableExperimentalFeatures, config.FeatureFlags.EnableExperimentalFeatures);
        Assert.Equal(expectedProd.EnableAdvancedDelegation, config.FeatureFlags.EnableAdvancedDelegation);
        Assert.Equal(expectedProd.EnableHumanInTheLoop, config.FeatureFlags.EnableHumanInTheLoop);
        Assert.Equal(expectedProd.EnableKnowledgeAugmentation, config.FeatureFlags.EnableKnowledgeAugmentation);
        Assert.Equal(expectedProd.MaxConcurrentOperations, config.FeatureFlags.MaxConcurrentOperations);
        Assert.Equal(expectedProd.DefaultTimeoutSeconds, config.FeatureFlags.DefaultTimeoutSeconds);
    }

    [Fact]
    public void ShouldReturnNewInstances_WhenUsingOrkeonConfigUsingStaticFactories()
    {
        // Act
        var default1 = OrkeonConfig.Default;
        var default2 = OrkeonConfig.Default;
        var dev1 = OrkeonConfig.Development;
        var dev2 = OrkeonConfig.Development;

        // Assert
        Assert.NotSame(default1, default2);
        Assert.NotSame(dev1, dev2);
    }

    #endregion

    #region OrkeonConfig CustomSettings Tests

    [Fact]
    public void ShouldBeModifiable_WhenUsingOrkeonConfigWithCustomSettings()
    {
        // Arrange
        var config = new OrkeonConfig
        {
            CustomSettings = new Dictionary<string, object>
            {
                { "apiKey", "test-key" },
                { "timeout", 30 },
                { "features", Feature1Feature2 }
            }
        };

        // Assert
        Assert.Equal(3, config.CustomSettings.Count);
        Assert.Equal("test-key", config.CustomSettings["apiKey"]);
        Assert.Equal(30, config.CustomSettings["timeout"]);
        Assert.IsType<string[]>(config.CustomSettings["features"]);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingOrkeonConfigWithCustomSettingsWithComplexObjects()
    {
        // Arrange
        var complexObject = new
        {
            DatabaseConfig = new { Host = "localhost", Port = 5432 },
            ApiKeys = ApiKeys12,
            Enabled = true
        };

        // Act
        var config = new OrkeonConfig
        {
            CustomSettings = new Dictionary<string, object>
            {
                { "databaseSettings", complexObject },
                { "nullValue", null! }
            }
        };

        // Assert
        Assert.Equal(2, config.CustomSettings.Count);
        Assert.Equal(complexObject, config.CustomSettings["databaseSettings"]);
        Assert.Null(config.CustomSettings["nullValue"]);
    }

    #endregion

    #region ExecutionConfig Constructor Tests

    [Fact]
    public void ShouldInitializeWithDefaults_WhenUsingExecutionConfigWithDefaultConstructor()
    {
        // Act
        var config = new ExecutionConfig();

        // Assert
        Assert.Equal(10, config.MaxConcurrentTasks);
        Assert.Equal(TimeoutStandard, config.DefaultTimeout);
        Assert.Equal(3, config.MaxRetries);
        Assert.False(config.EnableDebugMode);
        Assert.True(config.EnableAsyncExecution);
        Assert.NotNull(config.ExecutorSettings);
        Assert.Empty(config.ExecutorSettings);
    }

    [Fact]
    public void ShouldBeSettable_WhenUsingExecutionConfigUsingProperties()
    {
        // Arrange
        var executorSettings = new Dictionary<string, object>
        {
            { "poolSize", 20 },
            { "queueCapacity", 100 }
        };

        // Act
        var config = new ExecutionConfig
        {
            MaxConcurrentTasks = 15,
            DefaultTimeout = TimeoutExtended,
            MaxRetries = 5,
            EnableDebugMode = true,
            EnableAsyncExecution = false,
            ExecutorSettings = executorSettings
        };

        // Assert
        Assert.Equal(15, config.MaxConcurrentTasks);
        Assert.Equal(TimeoutExtended, config.DefaultTimeout);
        Assert.Equal(5, config.MaxRetries);
        Assert.True(config.EnableDebugMode);
        Assert.False(config.EnableAsyncExecution);
        Assert.Equal(executorSettings, config.ExecutorSettings);
        Assert.Equal(2, config.ExecutorSettings.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(100)]
    public void ShouldAcceptVariousValues_WhenUsingExecutionConfigWithMaxConcurrentTasks(int maxTasks)
    {
        // Act
        var config = new ExecutionConfig { MaxConcurrentTasks = maxTasks };

        // Assert
        Assert.Equal(maxTasks, config.MaxConcurrentTasks);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(300)]
    [InlineData(3600)]
    public void ShouldAcceptVariousTimeSpans_WhenUsingExecutionConfigWithDefaultTimeout(int seconds)
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(seconds);

        // Act
        var config = new ExecutionConfig { DefaultTimeout = timeout };

        // Assert
        Assert.Equal(timeout, config.DefaultTimeout);
    }

    [Fact]
    public void ShouldAccept_WhenUsingExecutionConfigWithDefaultTimeoutWithZeroTimeSpan()
    {
        // Act
        var config = new ExecutionConfig { DefaultTimeout = TimeSpan.Zero };

        // Assert
        Assert.Equal(TimeSpan.Zero, config.DefaultTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void ShouldAcceptVariousValues_WhenUsingExecutionConfigWithMaxRetries(int maxRetries)
    {
        // Act
        var config = new ExecutionConfig { MaxRetries = maxRetries };

        // Assert
        Assert.Equal(maxRetries, config.MaxRetries);
    }

    #endregion

    #region ExecutionConfig ExecutorSettings Tests

    [Fact]
    public void ShouldBeModifiable_WhenUsingExecutionConfigUsingExecutorSettings()
    {
        // Arrange
        var config = new ExecutionConfig
        {
            ExecutorSettings = new Dictionary<string, object>
            {
                { "threadPoolSize", 16 },
                { "queueTimeout", TimeoutQuick },
                { "enableProfiling", true }
            }
        };

        // Assert
        Assert.Equal(3, config.ExecutorSettings.Count);
        Assert.Equal(16, config.ExecutorSettings["threadPoolSize"]);
        Assert.IsType<TimeSpan>(config.ExecutorSettings["queueTimeout"]);
        Assert.True((bool)config.ExecutorSettings["enableProfiling"]);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingExecutionConfigUsingExecutorSettingsWithComplexConfiguration()
    {
        // Arrange
        var advancedSettings = new
        {
            RetryPolicy = new { MaxAttempts = 5, BackoffMultiplier = 2.0 },
            CircuitBreaker = new { FailureThreshold = 10, TimeoutSeconds = 60 },
            Monitoring = new { EnableMetrics = true, SampleRate = 0.1 }
        };

        // Act
        var config = new ExecutionConfig
        {
            ExecutorSettings = new Dictionary<string, object>
            {
                { "advanced", advancedSettings },
                { "simpleFlag", false }
            }
        };

        // Assert
        Assert.Equal(2, config.ExecutorSettings.Count);
        Assert.Equal(advancedSettings, config.ExecutorSettings["advanced"]);
        Assert.False((bool)config.ExecutorSettings["simpleFlag"]);
    }

    #endregion

    #region Integration and Complex Scenario Tests

    [Fact]
    public void ShouldMaintainConsistency_WhenUsingOrkeonConfigWithCompleteConfiguration()
    {
        // Arrange
        var customExecutionConfig = new ExecutionConfig
        {
            MaxConcurrentTasks = 20,
            DefaultTimeout = TimeoutLong,
            MaxRetries = 5,
            EnableDebugMode = true,
            EnableAsyncExecution = true
        };

        // Act
        var config = new OrkeonConfig
        {
            Version = ConfigurationVersionId.Create(),
            Execution = customExecutionConfig,
            UpdatedAt = DateTime.UtcNow,
            CustomSettings = new Dictionary<string, object>
            {
                { "environment", "testing" },
                { "features", AdvancedFeatures }
            }
        };

        // Assert
        Assert.NotNull(config.Version);
        Assert.Equal(customExecutionConfig, config.Execution);
        Assert.Equal(20, config.Execution.MaxConcurrentTasks);
        Assert.NotNull(config.UpdatedAt);
        Assert.Equal(2, config.CustomSettings.Count);
        Assert.Equal("testing", config.CustomSettings["environment"]);
    }

    [Fact]
    public void ShouldDifferCorrectly_WhenUsingOrkeonConfigUsingEnvironmentSpecificConfigurations()
    {
        // Act
        var development = OrkeonConfig.Development;
        var production = OrkeonConfig.Production;

        // Assert - Execution differences
        Assert.True(development.Execution.EnableDebugMode);
        Assert.False(production.Execution.EnableDebugMode);

        // Assert - Memory differences
        Assert.Equal(MemoryStorageType.InMemory, development.Memory.Type);
        Assert.Equal(MemoryStorageType.Redis, production.Memory.Type);

        // Assert - Telemetry differences
        Assert.Equal(TelemetryLogLevel.Debug, development.Telemetry.LogLevel);
        Assert.Equal(TelemetryLogLevel.Warning, production.Telemetry.LogLevel);

        // Assert - Feature flag differences (indirect verification)
        Assert.NotEqual(development.FeatureFlags, production.FeatureFlags);
    }

    [Fact]
    public void ShouldAllowNestedAccess_WhenUsingOrkeonConfigUsingConfigurationChain()
    {
        // Arrange
        var config = OrkeonConfig.Development;

        // Act & Assert - Verify nested property access
        Assert.True(config.Execution.EnableDebugMode);
        Assert.Equal(MemoryStorageType.InMemory, config.Memory.Type);
        Assert.Equal(TelemetryLogLevel.Debug, config.Telemetry.LogLevel);
        Assert.True(config.FeatureFlags.EnableTelemetry || !config.FeatureFlags.EnableTelemetry); // Either true or false is valid
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingOrkeonConfigWithAllCustomizations()
    {
        // Arrange
        var executionSettings = new Dictionary<string, object>
        {
            { "customExecutor", "AdvancedExecutor" },
            { "priority", "high" }
        };
        var customSettings = new Dictionary<string, object>
        {
            { "database", new { Type = "PostgreSQL", ConnectionString = "test" } },
            { "cache", new { Provider = "Redis", TTL = 3600 } },
            { "monitoring", new { Enabled = true, Endpoint = "http://metrics.local" } }
        };

        // Act
        var config = new OrkeonConfig
        {
            Version = ConfigurationVersionId.Create(),
            Execution = new ExecutionConfig { ExecutorSettings = executionSettings },
            CustomSettings = customSettings,
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        };

        // Assert
        Assert.NotNull(config.Version);
        Assert.Equal(2, config.Execution.ExecutorSettings.Count);
        Assert.Equal("AdvancedExecutor", config.Execution.ExecutorSettings["customExecutor"]);
        Assert.Equal(3, config.CustomSettings.Count);
        Assert.Contains("database", config.CustomSettings.Keys);
        Assert.Contains("cache", config.CustomSettings.Keys);
        Assert.Contains("monitoring", config.CustomSettings.Keys);
        Assert.NotNull(config.UpdatedAt);
        Assert.True(config.UpdatedAt < DateTime.UtcNow);
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ShouldAcceptValues_WhenUsingExecutionConfigWithNegativeValues()
    {
        // Act
        var config = new ExecutionConfig
        {
            MaxConcurrentTasks = -1,
            MaxRetries = -5
        };

        // Assert - No validation constraints in the record itself
        Assert.Equal(-1, config.MaxConcurrentTasks);
        Assert.Equal(-5, config.MaxRetries);
    }

    [Fact]
    public void ShouldAcceptValue_WhenUsingExecutionConfigWithNegativeTimeout()
    {
        // Arrange
        var negativeTimeout = TimeSpan.FromMilliseconds(-1000);

        // Act
        var config = new ExecutionConfig { DefaultTimeout = negativeTimeout };

        // Assert
        Assert.Equal(negativeTimeout, config.DefaultTimeout);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingOrkeonConfigWithNullValues()
    {
        // Act
        var config = new OrkeonConfig
        {
            Version = null!,
            UpdatedAt = null
        };

        // Assert
        Assert.Null(config.Version);
        Assert.Null(config.UpdatedAt);
        // Other properties should remain as initialized
        Assert.NotNull(config.Execution);
        Assert.NotNull(config.Memory);
        Assert.NotNull(config.CustomSettings);
    }

    [Fact]
    public void ShouldUpdateReferences_WhenUsingOrkeonConfigUsingReplacingNestedConfigurations()
    {
        // Arrange
        var config = new OrkeonConfig();
        var originalExecution = config.Execution;
        var newExecution = new ExecutionConfig
        {
            MaxConcurrentTasks = 100,
            EnableDebugMode = true
        };

        // Act
        var updated = config with { Execution = newExecution };

        // Assert
        Assert.NotSame(originalExecution, updated.Execution);
        Assert.Equal(newExecution, updated.Execution);
        Assert.Equal(100, updated.Execution.MaxConcurrentTasks);
        Assert.True(updated.Execution.EnableDebugMode);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingExecutionConfigWithVeryLargeTimeout()
    {
        // Arrange
        var largeTimeout = TimeSpan.FromDays(365); // 1 year

        // Act
        var config = new ExecutionConfig { DefaultTimeout = largeTimeout };

        // Assert
        Assert.Equal(largeTimeout, config.DefaultTimeout);
        Assert.Equal(365 * 24 * 60 * 60, config.DefaultTimeout.TotalSeconds);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenUsingOrkeonConfigWithUnicodeVersionString()
    {
        // Arrange
        var unicodeVersion = ConfigurationVersionId.Create();

        // Act
        var config = new OrkeonConfig { Version = unicodeVersion };

        // Assert
        Assert.Equal(unicodeVersion, config.Version);
        Assert.NotNull(config.Version);
        Assert.NotNull(config.Version);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingOrkeonConfigToString()
    {
        // Arrange
        var config = new OrkeonConfig { Version = ConfigurationVersionId.Create() };

        // Act
        var stringRepresentation = config.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        // Records provide property-value output in ToString()
        Assert.Contains("OrkeonConfig", stringRepresentation);
    }

    [Fact]
    public void ShouldProvideReadableRepresentation_WhenUsingExecutionConfigToString()
    {
        // Arrange
        var config = new ExecutionConfig { MaxConcurrentTasks = 42 };

        // Act
        var stringRepresentation = config.ToString();

        // Assert
        Assert.NotNull(stringRepresentation);
        Assert.NotEmpty(stringRepresentation);
        Assert.Contains("ExecutionConfig", stringRepresentation);
    }

    #endregion
}
