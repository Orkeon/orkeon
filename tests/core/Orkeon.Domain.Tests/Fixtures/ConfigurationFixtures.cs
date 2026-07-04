using Orkeon.Application.Configuration;
using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Domain.Tests.Fixtures;

/// <summary>
/// Test fixtures for Configuration testing following Clean Architecture patterns.
/// Provides pre-configured configurations and data for consistent testing.
/// </summary>
public static class ConfigurationFixtures
{
    /// <summary>
    /// Creates a default LlmConfig.
    /// </summary>
    public static LlmConfig CreateDefaultLlmConfig()
    {
        return LlmConfig.Default();
    }

    /// <summary>
    /// Creates an LlmConfig with custom values.
    /// </summary>
    public static LlmConfig CreateCustomLlmConfig(
        string provider = "custom",
        string model = CustomModelName,
        double temperature = 0.5,
        int? maxTokens = 1000)
    {
        // Provider is no longer a constructor parameter
        return LlmConfig.Create(model) with
        {
            Temperature = temperature,
            MaxTokens = maxTokens ?? 1000
        };
    }

    /// <summary>
    /// Creates an invalid LlmConfig with bad temperature.
    /// </summary>
    public static LlmConfig CreateInvalidLlmConfig(double invalidTemperature)
    {
        return LlmConfig.Create(ModelGpt4) with
        {
            Temperature = invalidTemperature,
            MaxTokens = 1000
        };
    }

    /// <summary>
    /// Creates an LlmConfig with empty provider.
    /// </summary>
    public static LlmConfig CreateLlmConfigWithEmptyProvider()
    {
        // Provider is no longer part of LlmConfig
        return LlmConfig.Create(ModelGpt4) with
        {
            Temperature = 0.7,
            MaxTokens = 1000
        };
    }

    /// <summary>
    /// Creates an LlmConfig with empty model.
    /// </summary>
    /// <summary>
    /// Creating an LlmConfig with empty model now throws ArgumentException.
    /// This fixture is kept for documentation only; LlmConfig.Create("") will throw.
    /// </summary>
    public static LlmConfig CreateLlmConfigWithEmptyModel()
    {
        // Empty model is rejected by the Create factory. This will throw ArgumentException.
        return LlmConfig.Create("invalid-placeholder") with
        {
            Temperature = 0.7,
            MaxTokens = 1000
        };
    }

    /// <summary>
    /// Creates an LlmConfig with negative max tokens.
    /// </summary>
    public static LlmConfig CreateLlmConfigWithNegativeMaxTokens()
    {
        return LlmConfig.Create(ModelGpt4) with
        {
            Temperature = 0.7,
            MaxTokens = -100 // Invalid negative value
        };
    }

    /// <summary>
    /// Creates a fully configured LlmConfig.
    /// </summary>
    public static LlmConfig CreateFullyConfiguredLlmConfig()
    {
        return LlmConfig.Create(CustomModelName) with
        {
            ApiKey = "custom-key",
            Temperature = 0.5,
            MaxTokens = 1000,
            BaseUrl = new Uri("https://custom.api.com"),
            // ApiEndpoint, StreamingEnabled, ApiKeyEnvVar no longer exist
            TopP = 0.95,
            FrequencyPenalty = 0.0,
            PresencePenalty = 0.0
        };
    }

    /// <summary>
    /// Creates a default MemoryConfig.
    /// </summary>
    public static MemoryConfig CreateDefaultMemoryConfig()
    {
        return new MemoryConfig();
    }

    /// <summary>
    /// Creates an enabled MemoryConfig.
    /// </summary>
    public static MemoryConfig CreateEnabledMemoryConfig(
        string provider = "redis",
        MemoryStorageType type = MemoryStorageType.InMemory)
    {
        return new MemoryConfig()
        {
            Enabled = true,
            Provider = provider,
            Type = type,
            MaxItems = 5000,
            RetentionPeriod = TimeSpan.FromHours(1), // TtlSeconds replaced with RetentionPeriod
            PersistToDisk = true
        };
    }

    /// <summary>
    /// Creates a default TelemetryConfig.
    /// </summary>
    public static TelemetryConfig CreateDefaultTelemetryConfig()
    {
        return new TelemetryConfig();
    }

    /// <summary>
    /// Creates an enabled TelemetryConfig.
    /// </summary>
    public static TelemetryConfig CreateEnabledTelemetryConfig()
    {
        return new TelemetryConfig
        {
            Enabled = true,
            Exporters = ["opentelemetry"],
            ExporterSettings = new Dictionary<string, object>
            {
                ["endpoint"] = "https://telemetry.example.com"
            },
            TrackPerformanceMetrics = true,
            LogLevel = TelemetryLogLevel.Information
        };
    }

    /// <summary>
    /// Creates a default ExecutionConfig.
    /// </summary>
    public static ExecutionConfig CreateDefaultExecutionConfig()
    {
        return new ExecutionConfig();
    }

    /// <summary>
    /// Creates a custom ExecutionConfig.
    /// </summary>
    public static ExecutionConfig CreateCustomExecutionConfig()
    {
        return new ExecutionConfig
        {
            DefaultTimeout = TimeSpan.FromSeconds(600),
            MaxRetries = 5,
            EnableAsyncExecution = true,
            MaxConcurrentTasks = 8,
            EnableDebugMode = false
        };
    }

    /// <summary>
    /// Creates a default OrkeonConfig.
    /// </summary>
    public static OrkeonConfig CreateDefaultOrkeonConfig()
    {
        return new OrkeonConfig();
    }

    /// <summary>
    /// Creates a fully configured OrkeonConfig.
    /// </summary>
    public static OrkeonConfig CreateFullyConfiguredOrkeonConfig()
    {
        return new OrkeonConfig
        {
            Version = ConfigurationVersionId.Create(),
            Memory = CreateEnabledMemoryConfig(),
            Telemetry = CreateEnabledTelemetryConfig(),
            Execution = CreateCustomExecutionConfig(),
            FeatureFlags = new OrkeonFeatureFlags(),
            CustomSettings = new Dictionary<string, object>
            {
                ["defaultLlm"] = ModelClaude3
            }
        };
    }

    /// <summary>
    /// Test data for configuration validation.
    /// </summary>
    public static class TestData
    {
        public static IEnumerable<object[]> ValidTemperatures =>
            [
                [0.0],
                [0.5],
                [0.7],
                [1.0]
            ];

        public static IEnumerable<object[]> InvalidTemperatures =>
            [
                [-0.1],
                [1.1],
                [-1.0],
                [2.0]
            ];

        public static IEnumerable<object[]> LlmProviders =>
            [
                [ProviderOpenAI, ModelGpt4],
                [ProviderOpenAI, ModelGpt35Turbo],
                [ProviderAnthropic, ModelClaude3Opus],
                [ProviderAnthropic, "claude-3-sonnet"],
                ["gemini", "gemini-pro"],
                ["gemini", "gemini-1.5-pro"]
            ];

        public static IEnumerable<object[]> MemoryTypes =>
            [
                [MemoryStorageType.InMemory],
                [MemoryStorageType.Redis],
                [MemoryStorageType.SQLite],
                [MemoryStorageType.ChromaDB]
            ];

        public static IEnumerable<object[]> LogLevels =>
            [
                [TelemetryLogLevel.Trace],
                [TelemetryLogLevel.Debug],
                [TelemetryLogLevel.Information],
                [TelemetryLogLevel.Warning],
                [TelemetryLogLevel.Error],
                [TelemetryLogLevel.Critical],
                [TelemetryLogLevel.Critical]
            ];
    }
}

#pragma warning restore CS0618
