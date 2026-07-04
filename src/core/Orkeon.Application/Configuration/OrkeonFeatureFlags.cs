using Orkeon.Application.Constants.Execution;

namespace Orkeon.Application.Configuration;

/// <summary>
/// Feature flags for Orkeon functionality.
/// </summary>
public sealed record OrkeonFeatureFlags
{
    /// <summary>
    /// Enables event-driven architecture.
    /// </summary>
    public bool EnableEvents { get; init; } = true;

    /// <summary>
    /// Enables telemetry collection.
    /// </summary>
    public bool EnableTelemetry { get; init; } = true;

    /// <summary>
    /// Enables advanced delegation features.
    /// </summary>
    public bool EnableAdvancedDelegation { get; init; }

    /// <summary>
    /// Enables memory persistence.
    /// </summary>
    public bool EnableMemoryPersistence { get; init; } = true;

    /// <summary>
    /// Enables tool validation.
    /// </summary>
    public bool EnableToolValidation { get; init; } = true;

    /// <summary>
    /// Enables async task execution.
    /// </summary>
    public bool EnableAsyncExecution { get; init; } = true;

    /// <summary>
    /// Enables human-in-the-loop features.
    /// </summary>
    public bool EnableHumanInTheLoop { get; init; }

    /// <summary>
    /// Enables knowledge augmentation.
    /// </summary>
    public bool EnableKnowledgeAugmentation { get; init; }

    /// <summary>
    /// Enables performance metrics collection.
    /// </summary>
    public bool EnablePerformanceMetrics { get; init; } = true;

    /// <summary>
    /// Enables experimental features.
    /// </summary>
    public bool EnableExperimentalFeatures { get; init; }

    /// <summary>
    /// Maximum number of concurrent operations.
    /// </summary>
    public int MaxConcurrentOperations { get; init; } = 10;

    /// <summary>
    /// Default timeout for operations in seconds.
    /// </summary>
    public int DefaultTimeoutSeconds { get; init; } = ExecutionDefaults.DefaultMaxExecutionSeconds;

    /// <summary>
    /// Creates default feature flags.
    /// </summary>
    public static OrkeonFeatureFlags Default => new();

    /// <summary>
    /// Creates feature flags for development.
    /// </summary>
    public static OrkeonFeatureFlags Development => new()
    {
        EnableTelemetry = false,
        EnableExperimentalFeatures = true,
        EnableAdvancedDelegation = true,
        EnableHumanInTheLoop = true,
        EnableKnowledgeAugmentation = true
    };

    /// <summary>
    /// Creates feature flags for production.
    /// </summary>
    public static OrkeonFeatureFlags Production => new()
    {
        EnableTelemetry = true,
        EnableExperimentalFeatures = false,
        EnableAdvancedDelegation = false,
        EnableHumanInTheLoop = false,
        EnableKnowledgeAugmentation = false,
        MaxConcurrentOperations = 50,
        DefaultTimeoutSeconds = 600
    };
}
